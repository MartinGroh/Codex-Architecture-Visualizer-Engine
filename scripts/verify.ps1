[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:PYTHONDONTWRITEBYTECODE = '1'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$pluginPath = Join-Path $repoRoot 'plugins\cave'
$validationScript = Join-Path $PSScriptRoot 'validate-plugin.ps1'
$metadataValidationScript = Join-Path $PSScriptRoot 'validate-plugin-metadata.ps1'
$publicReadinessScript = Join-Path $PSScriptRoot 'audit-public-readiness.ps1'
$codeGraphFallback = Join-Path $env:LOCALAPPDATA 'codegraph\current\bin\codegraph.cmd'
$codeGraphCommand = Get-Command codegraph -ErrorAction SilentlyContinue
$codeGraphExecutable = if ($null -ne $codeGraphCommand) { $codeGraphCommand.Source } else { $codeGraphFallback }
$healthPort = 5097
$healthBaseUri = "http://127.0.0.1:$healthPort"
$activityHook = Join-Path $pluginPath 'scripts\cave_activity_hook.py'
$hooksManifest = Join-Path $pluginPath 'hooks\hooks.json'
$env:PLUGIN_ROOT = $pluginPath

if (-not (Test-Path -LiteralPath $codeGraphExecutable)) {
    throw 'CodeGraph is not installed. Install its official standalone bundle before verification.'
}

foreach ($requiredVerificationScript in @(
    $validationScript,
    $metadataValidationScript,
    $publicReadinessScript
)) {
    if (-not (Test-Path -LiteralPath $requiredVerificationScript)) {
        throw "A repository verification entry point is missing: $requiredVerificationScript"
    }
}

foreach ($requiredActivityPath in @($hooksManifest, $activityHook)) {
    if (-not (Test-Path -LiteralPath $requiredActivityPath)) {
        throw "The CAVE activity bridge is incomplete: $requiredActivityPath"
    }
}

$hookConfiguration = Get-Content -LiteralPath $hooksManifest -Raw | ConvertFrom-Json
if ($null -eq $hookConfiguration.hooks.UserPromptSubmit) {
    throw 'The CAVE activity bridge must subscribe to UserPromptSubmit for prompt-boundary resets.'
}
$windowsActivityHookCommand = $hookConfiguration.hooks.UserPromptSubmit[0].hooks[0].commandWindows

foreach ($eventConfiguration in $hookConfiguration.hooks.PSObject.Properties.Value) {
    foreach ($matcherGroup in @($eventConfiguration)) {
        foreach ($handler in @($matcherGroup.hooks)) {
            if ($handler.commandWindows -match '%PLUGIN_ROOT%' -or $handler.commandWindows -match '\$env:PLUGIN_ROOT') {
                throw 'The CAVE Windows hook launcher must not depend on the invoking shell syntax.'
            }
        }
    }
}

if (Test-Path -LiteralPath (Join-Path $pluginPath 'hooks.json')) {
    throw 'Plugin hooks must use the Codex-discovered hooks/hooks.json path, not a root hooks.json file.'
}

if (($hookConfiguration | ConvertTo-Json -Depth 12) -match 'timeoutSec') {
    throw 'The CAVE hook manifest must use the documented timeout field.'
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Command,

        [Parameter(Mandatory)]
        [string] $Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try {
    Invoke-CheckedNative { pwsh -NoProfile -File $publicReadinessScript } 'Public-readiness audit'
    Invoke-CheckedNative { pwsh -NoProfile -File $metadataValidationScript } 'Repository plugin metadata validation'
    Invoke-CheckedNative { pwsh -NoProfile -File resources/agent-flow-light/embedded/test.ps1 } 'Portable Agent Flow C/C++ client tests'
    Invoke-CheckedNative { python -m unittest discover -s plugins/cave/scripts -p 'test_*.py' } 'CAVE activity hook tests'
    Invoke-CheckedNative { pwsh -NoProfile -File plugins/cave/scripts/test_cave_hook_launcher.ps1 } 'CAVE hook launcher fallback tests'
    Invoke-CheckedNative { pwsh -NoProfile -File plugins/cave/scripts/test_cave_hook_observation.ps1 } 'CAVE current-session hook observation tests'
    Invoke-CheckedNative { & $codeGraphExecutable sync } 'CodeGraph synchronization'
    Invoke-CheckedNative { & $codeGraphExecutable status } 'CodeGraph status check'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui ci } 'Frontend dependency restore'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui audit --audit-level=high } 'Frontend dependency vulnerability audit'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui run build } 'Frontend build'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui run test } 'Frontend tests'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui run lint } 'Frontend lint'
    Invoke-CheckedNative { dotnet build CAVE.slnx } '.NET build'
    Invoke-CheckedNative { dotnet list CAVE.slnx package --vulnerable --include-transitive } '.NET dependency vulnerability audit'
    Invoke-CheckedNative { dotnet test CAVE.slnx --no-build } '.NET tests'
    Invoke-CheckedNative { dotnet publish src/Cave.Mcp/Cave.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output plugins/cave/server } 'CAVE MCP publish'
    Invoke-CheckedNative { dotnet publish src/Cave.Host/Cave.Host.csproj --configuration Release --runtime win-x64 --self-contained false --output plugins/cave/server } 'CAVE machine viewer publish'
    foreach ($requiredExecutable in @('Cave.Mcp.exe', 'Cave.Host.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginPath "server\$requiredExecutable"))) {
            throw "The published plugin is missing $requiredExecutable."
        }
    }
    Invoke-CheckedNative { pwsh -NoProfile -File $validationScript -PluginPath $pluginPath } 'Plugin validation'

    $hostProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', 'src/Cave.Host', '--no-build', '--', '--urls', $healthBaseUri) `
        -Environment @{ 'Cave__WorkspaceRoot' = $repoRoot } `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -PassThru
    $verificationEventPaths = @()
    $verificationConversationPaths = @()

    try {
        $ready = $false
        foreach ($attempt in 1..40) {
            if ($hostProcess.HasExited) {
                throw "CAVE host exited before becoming ready (exit code $($hostProcess.ExitCode))."
            }

            try {
                $health = Invoke-RestMethod -Uri "$healthBaseUri/health" -TimeoutSec 2
                $ready = $true
                break
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }

        if (-not $ready -or ([string] $health).Trim() -ne 'Healthy') {
            throw 'CAVE health endpoint did not report Healthy.'
        }

        $runtime = Invoke-RestMethod -Uri "$healthBaseUri/api/runtime" -TimeoutSec 5
        if ($runtime.viewerProtocolVersion -ne 2) {
            throw 'The CAVE host did not expose the expected viewer compatibility contract.'
        }

        $workspaceCatalog = Invoke-RestMethod -Uri "$healthBaseUri/api/workspaces" -TimeoutSec 5
        $registeredWorkspace = $workspaceCatalog.workspaces | Where-Object {
            $_.rootPath -eq $repoRoot -and $_.isAvailable
        } | Select-Object -First 1
        if ($null -eq $registeredWorkspace -or [string]::IsNullOrWhiteSpace($registeredWorkspace.workspaceId)) {
            throw 'The machine workspace dashboard did not list the verification repository.'
        }

        $workspaceQuery = [uri]::EscapeDataString($registeredWorkspace.workspaceId)
        $snapshotUri = "$healthBaseUri/api/snapshot?workspace=$workspaceQuery"
        $snapshot = Invoke-RestMethod -Uri $snapshotUri -TimeoutSec 5
        if ($snapshot.snapshot.metadata.sourceKind -ne 'CodeGraph' -or -not $snapshot.snapshot.metadata.isLive) {
            throw 'The live snapshot did not expose CodeGraph provenance correctly.'
        }

        if ($snapshot.version -lt 1 -or $snapshot.snapshot.graph.nodes.Count -eq 0 -or $snapshot.snapshot.graph.relations.Count -eq 0) {
            throw 'The live snapshot returned an empty or unversioned graph.'
        }

        $httpApiRelation = $snapshot.snapshot.graph.relations | Where-Object {
            $_.kind -eq 'HttpApi' -and
            $_.sourceId -eq 'project:src/Cave.Ui:cave-ui' -and
            $_.targetId -eq 'project:src/Cave.Host:Cave.Host'
        }
        if ($null -eq $httpApiRelation -or
            $httpApiRelation.confidence -ne 'Inferred' -or
            $httpApiRelation.evidenceCount -lt 2) {
            throw 'The live snapshot did not infer the UI-to-host HTTP/SSE contract.'
        }

        if ($snapshot.snapshot.git.status -ne 'Ready' -or
            [string]::IsNullOrWhiteSpace($snapshot.snapshot.git.baseline.resolvedSha) -or
            [string]::IsNullOrWhiteSpace($snapshot.snapshot.git.worktree.headSha)) {
            throw 'The live snapshot did not expose ready Git evidence with exact baseline and worktree identities.'
        }

        if ($null -eq $snapshot.snapshot.activity -or $null -eq $snapshot.snapshot.activity.agents) {
            throw 'The live snapshot did not expose the independent activity overlay contract.'
        }

        if ($null -eq $snapshot.snapshot.conversation -or
            $snapshot.snapshot.conversation.PSObject.Properties.Name -notcontains 'sharingEnabled') {
            throw 'The live snapshot did not expose the opt-in conversation overlay contract.'
        }

        $info = Invoke-RestMethod -Uri "$healthBaseUri/api/info" -TimeoutSec 20
        if ($info.viewerProtocolVersion -ne 2 -or
            $null -eq $info.usage -or
            [string]::IsNullOrWhiteSpace($info.usage.status)) {
            throw 'The live info endpoint did not expose the viewer and Codex usage contract.'
        }

        $verificationSession = "cave-verify-$([guid]::NewGuid().ToString('N'))"
        $activityPayload = @{
            hook_event_name = 'PostToolUse'
            session_id = $verificationSession
            turn_id = 'verification-turn'
            cwd = $repoRoot
            tool_name = 'apply_patch'
            tool_input = @{
                file_path = Join-Path $repoRoot 'src\Cave.Application\ArchitectureSnapshotService.cs'
            }
            tool_response = @{ success = $true }
        } | ConvertTo-Json -Depth 8 -Compress
        Invoke-CheckedNative {
            $activityPayload | pwsh -NoProfile -Command $windowsActivityHookCommand
        } 'CAVE Windows activity hook bridge'

        $promptPayload = @{
            hook_event_name = 'UserPromptSubmit'
            session_id = $verificationSession
            turn_id = 'verification-next-turn'
            cwd = $repoRoot
            prompt = 'This prompt text must not be persisted.'
        } | ConvertTo-Json -Depth 4 -Compress
        Invoke-CheckedNative {
            $promptPayload | pwsh -NoProfile -Command $windowsActivityHookCommand
        } 'CAVE Windows prompt-boundary hook bridge'

        $activityReady = $false
        foreach ($attempt in 1..30) {
            $activitySnapshot = Invoke-RestMethod -Uri $snapshotUri -TimeoutSec 5
            $matchingAgent = $activitySnapshot.snapshot.activity.agents | Where-Object {
                $_.agentId -eq "session:$verificationSession" -and $_.hasObservedActivity
            }
            $instructionMarker = $activitySnapshot.snapshot.activity.latestInstruction
            if ($activitySnapshot.version -gt $snapshot.version -and
                $activitySnapshot.activityChanged -and
                $null -ne $matchingAgent -and
                $instructionMarker.id -eq "$verificationSession`:verification-next-turn") {
                $activityReady = $true
                break
            }

            Start-Sleep -Milliseconds 100
        }

        if (-not $activityReady) {
            throw 'The hook event did not advance the live activity overlay version.'
        }

        $activityDirectory = Join-Path $repoRoot '.cave\activity\inbox'
        $verificationEventPaths = @(Get-ChildItem -LiteralPath $activityDirectory -Filter '*.json' | Where-Object {
            try {
                (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).sessionId -eq $verificationSession
            }
            catch {
                $false
            }
        } | Select-Object -ExpandProperty FullName)
        $conversationDirectory = Join-Path $repoRoot '.cave\conversation\inbox'
        if (Test-Path -LiteralPath $conversationDirectory -PathType Container) {
            $verificationConversationPaths = @(Get-ChildItem -LiteralPath $conversationDirectory -Filter '*.json' | Where-Object {
                try {
                    (Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json).sessionId -eq $verificationSession
                }
                catch {
                    $false
                }
            } | Select-Object -ExpandProperty FullName)
        }

        Write-Host "Verified live CAVE endpoints at $healthBaseUri"
    }
    finally {
        if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
            Stop-Process -Id $hostProcess.Id
            $hostProcess.WaitForExit()
        }

        foreach ($verificationEventPath in $verificationEventPaths) {
            if (Test-Path -LiteralPath $verificationEventPath) {
                Remove-Item -LiteralPath $verificationEventPath
            }
        }
        foreach ($verificationConversationPath in $verificationConversationPaths) {
            if (Test-Path -LiteralPath $verificationConversationPath) {
                Remove-Item -LiteralPath $verificationConversationPath
            }
        }
    }
}
finally {
    Pop-Location
}

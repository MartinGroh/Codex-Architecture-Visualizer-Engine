[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pluginRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$hooksManifest = Join-Path $pluginRoot 'hooks\hooks.json'
$activityHook = Join-Path $pluginRoot 'scripts\cave_activity_hook.py'
$hookConfiguration = Get-Content -LiteralPath $hooksManifest -Raw | ConvertFrom-Json
$handlers = @(
    foreach ($eventConfiguration in $hookConfiguration.hooks.PSObject.Properties.Value) {
        foreach ($matcherGroup in @($eventConfiguration)) {
            foreach ($handler in @($matcherGroup.hooks)) {
                $handler
            }
        }
    }
)

$windowsCommands = @($handlers.commandWindows | Sort-Object -Unique)
if ($windowsCommands.Count -ne 1) {
    throw 'Every CAVE lifecycle event must use one stable Windows hook launcher definition.'
}
$fallbackPattern = ".codex/plugins/cache/*/cave/*/scripts/cave_activity_hook.py"
foreach ($handler in $handlers) {
    if (-not ([string] $handler.command).Contains($fallbackPattern) -or
        -not ([string] $handler.commandWindows).Contains($fallbackPattern)) {
        throw 'CAVE hook launchers must recover through the machine plugin cache when their original PLUGIN_ROOT is removed.'
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "cave-hook-launcher-$([guid]::NewGuid().ToString('N'))"
$fakeHome = Join-Path $temporaryRoot 'home'
$fakeLocalData = Join-Path $temporaryRoot 'local'
$fallbackRoot = Join-Path $fakeHome '.codex\plugins\cache\personal\cave\9.9.9+codex.99999999999999'
$fallbackRunner = Join-Path $fallbackRoot 'scripts\cave_activity_hook.py'
$workspaceRoot = Join-Path $temporaryRoot 'workspace'
New-Item -ItemType Directory -Path (Split-Path -Parent $fallbackRunner) -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $workspaceRoot '.git') -Force | Out-Null
Copy-Item -LiteralPath $activityHook -Destination $fallbackRunner

$previousEnvironment = @{
    USERPROFILE = [Environment]::GetEnvironmentVariable('USERPROFILE', 'Process')
    LOCALAPPDATA = [Environment]::GetEnvironmentVariable('LOCALAPPDATA', 'Process')
    PLUGIN_ROOT = [Environment]::GetEnvironmentVariable('PLUGIN_ROOT', 'Process')
}

try {
    $env:USERPROFILE = $fakeHome
    $env:LOCALAPPDATA = $fakeLocalData
    $env:PLUGIN_ROOT = Join-Path $temporaryRoot 'removed-plugin-version'
    $payload = @{
        hook_event_name = 'PreToolUse'
        session_id = 'fallback-session'
        turn_id = 'fallback-turn'
        cwd = $workspaceRoot
        tool_name = 'Bash'
        tool_input = @{ command = 'Get-Content src/Probe.cs' }
    } | ConvertTo-Json -Depth 5 -Compress

    $hookOutput = @($payload | pwsh -NoProfile -Command $windowsCommands[0] 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "The missing-PLUGIN_ROOT fallback hook failed with exit code $LASTEXITCODE.`n$($hookOutput -join [Environment]::NewLine)"
    }

    $eventPath = Get-ChildItem -LiteralPath (Join-Path $workspaceRoot '.cave\activity\inbox') -Filter '*.json' -File |
        Select-Object -First 1
    if ($null -eq $eventPath) {
        throw 'The fallback hook launcher did not write an activity event.'
    }
    $activityEvent = Get-Content -LiteralPath $eventPath.FullName -Raw | ConvertFrom-Json
    if ($activityEvent.sessionId -ne 'fallback-session' -or
        $activityEvent.kind -ne 'PreToolUse' -or
        $activityEvent.phase -ne 'Reading') {
        throw 'The fallback hook launcher wrote the wrong session activity contract.'
    }
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        $value = $previousEnvironment[$name]
        if ($null -eq $value) {
            Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -LiteralPath "Env:$name" -Value $value
        }
    }

    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        $canonicalTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $canonicalTemporaryBase = [IO.Path]::TrimEndingDirectorySeparator(
            [IO.Path]::GetFullPath([IO.Path]::GetTempPath())) + [IO.Path]::DirectorySeparatorChar
        if (-not $canonicalTemporaryRoot.StartsWith($canonicalTemporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove test data outside the temporary directory: $canonicalTemporaryRoot"
        }
        Remove-Item -LiteralPath $canonicalTemporaryRoot -Recurse
    }
}

Write-Host 'CAVE hook launcher fallback tests passed.'

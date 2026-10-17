[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'plugin-tooling.psm1') -Force
$testParent = Join-Path $repoRoot 'artifacts/test-plugin-tooling'
$testRoot = Join-Path $testParent ([guid]::NewGuid().ToString('N'))
$fixtureRoot = Join-Path $testRoot 'repository with spaces'
$pluginRoot = Join-Path $fixtureRoot 'plugins/cave'
$manifestPath = Join-Path $pluginRoot '.codex-plugin/plugin.json'
$hooksPath = Join-Path $pluginRoot 'hooks/hooks.json'
$marketplacePath = Join-Path $fixtureRoot '.agents/plugins/marketplace.json'
$script:checks = 0

function Assert-Equal($Actual, $Expected, [string] $Message) {
    if ($Actual -cne $Expected) { throw "$Message Expected '$Expected', got '$Actual'." }
    $script:checks++
}
function Assert-Rejected([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Accepted invalid input: $Message" }
    $script:checks++
}
function Write-Json([string] $Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 32), [Text.UTF8Encoding]::new($false))
}
function Reset-Fixture {
    foreach ($relative in @('.codex-plugin/plugin.json', 'hooks/hooks.json', '.mcp.json')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "plugins/cave/$relative") -Destination (Join-Path $pluginRoot $relative) -Force
    }
    Copy-Item -LiteralPath (Join-Path $repoRoot '.agents/plugins/marketplace.json') -Destination $marketplacePath -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'plugins/cave/skills/open/SKILL.md') -Destination (Join-Path $pluginRoot 'skills/open/SKILL.md') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'plugins/cave/skills/open/agents/openai.yaml') -Destination (Join-Path $pluginRoot 'skills/open/agents/openai.yaml') -Force
}
function Test-Fixture { Test-CavePluginPackage -PluginPath $pluginRoot -RepositoryRoot $fixtureRoot }
function Test-Mutation([scriptblock] $Mutation, [scriptblock] $Check = { Test-Fixture }, [string] $Message) {
    Reset-Fixture
    & $Mutation
    Assert-Rejected $Check $Message
}

$originalDesktopPath = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH')
$originalSearchPath = $env:PATH
try {
    New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
    foreach ($directory in @('.codex-plugin', 'skills', 'hooks', 'assets', 'scripts')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot "plugins/cave/$directory") -Destination $pluginRoot -Recurse
    }
    foreach ($relative in @('VERSION', 'Directory.Build.props', 'src/Cave.Ui/package.json', '.agents/plugins/marketplace.json')) {
        $target = Join-Path $fixtureRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination $target
    }
    Reset-Fixture
    Assert-Equal (Test-Fixture).name 'cave' 'Valid source package.'
    Assert-Equal (Get-CaveRepositoryMarketplace $fixtureRoot $pluginRoot) 'personal' 'Repository marketplace identity.'
    Assert-Rejected { Test-CavePluginPackage -PluginPath $pluginRoot -RepositoryRoot $fixtureRoot -RequireBinaries } 'Packaged binaries absent.'
    New-Item -ItemType Directory -Path (Join-Path $pluginRoot 'server') | Out-Null
    foreach ($binary in @('Cave.Mcp.exe', 'Cave.Host.exe')) { [IO.File]::WriteAllText((Join-Path $pluginRoot "server/$binary"), 'fixture') }
    Assert-Equal (Test-CavePluginPackage -PluginPath $pluginRoot -RepositoryRoot $fixtureRoot -RequireBinaries).name 'cave' 'Packaged entry points.'

    Test-Mutation { [IO.File]::WriteAllText($manifestPath, '{bad') } -Message 'Malformed JSON.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw; [IO.File]::WriteAllText($manifestPath, $value.Replace('"name": "cave"', '"name": "cave", "name": "other"')) } -Message 'Duplicate JSON property.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.version = '0.3.0+codex.bad'; Write-Json $manifestPath $value } -Message 'Malformed cachebuster.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.version = '0.3.0-01'; Write-Json $manifestPath $value } -Message 'Invalid SemVer prerelease.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.version = '1.0.0'; Write-Json $manifestPath $value } -Message 'Repository version mismatch.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.interface.logo = './../outside.png'; Write-Json $manifestPath $value } -Message 'Escaping asset path.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.interface.logo = './missing.png'; Write-Json $manifestPath $value } -Message 'Missing asset.'
    Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.mcpServers = './../outside.json'; Write-Json $manifestPath $value } -Message 'Escaping MCP reference.'
    Test-Mutation { $path = Join-Path $pluginRoot '.mcp.json'; $value = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable; $value.mcpServers.cave.command = 'codex'; Write-Json $path $value } -Message 'Foreign MCP command.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Remove('Interrupt'); Write-Json $hooksPath $value } -Message 'Missing required event.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Stop = @(); Write-Json $hooksPath $value } -Message 'Empty matcher groups.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Stop[0].hooks = @(); Write-Json $hooksPath $value } -Message 'Empty handlers.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Stop[0].hooks[0].timeoutSec = 5; Write-Json $hooksPath $value } -Message 'Unsupported timeoutSec.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Stop[0].hooks[0].timeout = $true; Write-Json $hooksPath $value } -Message 'Boolean timeout.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Stop[0].hooks[0].commandWindows = 'python %PLUGIN_ROOT%/runner.py'; Write-Json $hooksPath $value } -Message 'Shell-specific Windows hook.'
    Test-Mutation { $value = Get-Content $hooksPath -Raw | ConvertFrom-Json -AsHashtable; $value.hooks.Stop[0].hooks[0].commandWindows = 'python -c "import os;os.environ.get(''PLUGIN_ROOT'')"'; Write-Json $hooksPath $value } -Message 'Missing hook-runner recovery.'
    Test-Mutation { $path = Join-Path $pluginRoot 'skills/open/SKILL.md'; [IO.File]::WriteAllText($path, "---`nname: wrong`ndescription: x`n---`nBody`n") } -Message 'Mismatched skill identity.'
    Test-Mutation { $path = Join-Path $pluginRoot 'skills/open/SKILL.md'; [IO.File]::WriteAllText($path, "---`nname: open`ndescription: [broken`n---`nBody`n") } -Message 'Unsupported or malformed YAML.'
    Test-Mutation { $path = Join-Path $pluginRoot 'skills/open/SKILL.md'; [IO.File]::WriteAllText($path, "---`nname: open`nname: open`ndescription: value`n---`nBody`n") } -Message 'Duplicate frontmatter.'
    Test-Mutation { $path = Join-Path $pluginRoot 'skills/open/agents/openai.yaml'; [IO.File]::WriteAllText($path, 'interface: [broken') } -Message 'Malformed skill interface.'
    Test-Mutation { $value = Get-Content $marketplacePath -Raw | ConvertFrom-Json -AsHashtable; $value.name = '../escape'; Write-Json $marketplacePath $value } { Get-CaveRepositoryMarketplace $fixtureRoot $pluginRoot } 'Invalid marketplace identity.'
    Test-Mutation { $value = Get-Content $marketplacePath -Raw | ConvertFrom-Json -AsHashtable; $value.plugins += $value.plugins[0]; Write-Json $marketplacePath $value } { Get-CaveRepositoryMarketplace $fixtureRoot $pluginRoot } 'Duplicate marketplace entry.'
    Test-Mutation { $value = Get-Content $marketplacePath -Raw | ConvertFrom-Json -AsHashtable; $value.plugins[0].source.path = './../foreign'; Write-Json $marketplacePath $value } { Get-CaveRepositoryMarketplace $fixtureRoot $pluginRoot } 'Escaping marketplace path.'
    if ($IsWindows) {
        Reset-Fixture
        $externalAssets = Join-Path $testRoot 'external-assets'
        New-Item -ItemType Directory -Path $externalAssets | Out-Null
        [IO.File]::WriteAllText((Join-Path $externalAssets 'icon.png'), 'fixture')
        New-Item -ItemType Junction -Path (Join-Path $pluginRoot 'assets/linked') -Target $externalAssets | Out-Null
        Test-Mutation { $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable; $value.interface.logo = './assets/linked/icon.png'; Write-Json $manifestPath $value } -Message 'Asset reference through a junction.'
    }

    Reset-Fixture
    $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $baseVersion = (Get-Content -LiteralPath (Join-Path $fixtureRoot 'VERSION') -Raw).Trim()
    $value.version = "${baseVersion}+codex.20260928092912"
    Write-Json $manifestPath $value
    $version = Set-CavePluginCachebuster -PluginPath $pluginRoot -Now ([DateTimeOffset]'2026-01-01T00:00:00Z')
    Assert-Equal $version "${baseVersion}+codex.20260928092913" 'Clock skew retains monotonic version.'
    Assert-Equal (Set-CavePluginCachebuster -PluginPath $pluginRoot -Now ([DateTimeOffset]'2026-01-01T00:00:00Z')) "${baseVersion}+codex.20260928092914" 'Repeated install retains monotonic version.'
    Assert-Equal (Set-CavePluginCachebuster -PluginPath $pluginRoot -Now ([DateTimeOffset]'2027-01-01T00:00:00Z')) "${baseVersion}+codex.20270101000000" 'New UTC time advances version.'
    Assert-Equal (Test-Fixture).name 'cave' 'Cachebuster preserves package metadata.'
    $value = Get-Content $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $value.version = '0.3.0+codex.99999999999999'
    Write-Json $manifestPath $value
    $previous = [IO.File]::ReadAllText($manifestPath)
    Assert-Rejected { Set-CavePluginCachebuster -PluginPath $pluginRoot } 'Cachebuster overflow.'
    Assert-Equal ([IO.File]::ReadAllText($manifestPath)) $previous 'Rejected version remains untouched.'

    $env:CODEX_CLI_PATH = 'relative/codex.exe'
    Assert-Rejected { Resolve-CaveCodexExecutable } 'Relative Desktop executable.'
    $env:CODEX_CLI_PATH = Join-Path $testRoot 'missing/codex.exe'
    Assert-Rejected { Resolve-CaveCodexExecutable } 'Missing Desktop executable.'
    $env:CODEX_CLI_PATH = ' '
    Assert-Rejected { Resolve-CaveCodexExecutable } 'Blank Desktop selection.'
    $env:CODEX_CLI_PATH = (Get-Process -Id $PID).Path
    Assert-Rejected { Resolve-CaveCodexExecutable } 'Unrelated executable is not Codex.'
    $env:CODEX_CLI_PATH = $null
    $env:PATH = $testRoot
    Assert-Rejected { Resolve-CaveCodexExecutable } 'Absent Desktop selection with no PATH command.'
    $env:PATH = $originalSearchPath
    if ($originalDesktopPath) {
        $env:CODEX_CLI_PATH = $originalDesktopPath
        Assert-Equal (Resolve-CaveCodexExecutable) (Resolve-Path -LiteralPath $originalDesktopPath).Path 'Desktop backend works without a PATH CLI.'
        $script:checks++
        Invoke-CaveCodexJson -Executable $originalDesktopPath -Arguments @('plugin', 'marketplace', 'list', '--json') -RequiredArrayProperty marketplaces | Out-Null
    }
    if ($IsWindows) {
        $pathStub = Join-Path $testRoot 'codex.cmd'
        $pathMarker = Join-Path $testRoot 'path-command-called.txt'
        [IO.File]::WriteAllText($pathStub, "@echo off`r`necho called>`"%~dp0path-command-called.txt`"`r`necho codex-cli fixture`r`n")
        $env:PATH = $testRoot
        $env:CODEX_CLI_PATH = Join-Path $testRoot 'missing-codex.exe'
        Assert-Rejected { Resolve-CaveCodexExecutable } 'Invalid Desktop selection with a valid PATH CLI.'
        Assert-Equal (Test-Path -LiteralPath $pathMarker) $false 'Invalid Desktop selection never consults PATH.'
        if ($originalDesktopPath) {
            $env:CODEX_CLI_PATH = $originalDesktopPath
            Assert-Equal (Resolve-CaveCodexExecutable) (Resolve-Path -LiteralPath $originalDesktopPath).Path 'Desktop selection wins over PATH.'
            Assert-Equal (Test-Path -LiteralPath $pathMarker) $false 'Valid Desktop selection never consults PATH.'
        }
        $env:CODEX_CLI_PATH = $null
        Assert-Equal (Resolve-CaveCodexExecutable) $pathStub 'Absent Desktop selection resolves PATH.'
        Assert-Equal (Test-Path -LiteralPath $pathMarker) $true 'Selected PATH CLI was validated.'
    }
    $env:PATH = $originalSearchPath

    # Native response failure checks use harmless PowerShell child commands, never plugin mutation.
    $shell = (Get-Process -Id $PID).Path
    Assert-Rejected { Invoke-CaveCodexJson -Executable $shell -Arguments @('-NoProfile', '-Command', 'exit 7') } 'Native nonzero response.'
    Assert-Rejected { Invoke-CaveCodexJson -Executable $shell -Arguments @('-NoProfile', '-Command', 'Write-Output invalid') } 'Malformed JSON response.'
    Assert-Rejected { Invoke-CaveCodexJson -Executable $shell -Arguments @('-NoProfile', '-Command', 'Write-Output ''{}''') -RequiredArrayProperty installed } 'Incompatible response shape.'

    # Exercise registration through a command-boundary simulator; never mutate the real marketplace.
    Reset-Fixture
    $previousRepository = Join-Path $testRoot 'previous repository'
    Copy-Item -LiteralPath $fixtureRoot -Destination $previousRepository -Recurse
    $registrationState = @{ Root = $fixtureRoot; OtherPlugin = $false; Mode = ''; Mutations = [Collections.Generic.List[string]]::new() }
    $runner = {
        param([string[]] $commandArguments)
        $command = $commandArguments[0..2] -join ' '
        if ($command -eq 'plugin marketplace list') {
            $rows = @()
            if ($null -ne $registrationState.Root) {
                $rows = @(@{ name = 'personal'; root = $registrationState.Root; marketplaceSource = @{ sourceType = 'local'; source = $registrationState.Root } })
            }
            return @{ marketplaces = $rows }
        }
        if ($commandArguments[0] -eq 'plugin' -and $commandArguments[1] -eq 'list') {
            $memberName = if ($registrationState.OtherPlugin) { 'unrelated' } else { 'cave' }
            return @{ installed = @(@{ name = $memberName; pluginId = "$memberName@personal"; marketplaceName = 'personal';
                marketplaceSource = @{ sourceType = 'local'; source = $previousRepository } }) }
        }
        $registrationState.Mutations.Add($command)
        if ($command -eq 'plugin marketplace remove') {
            $old = $registrationState.Root
            if ($registrationState.Mode -ne 'RemoveNoop') { $registrationState.Root = $null }
            if ($registrationState.Mode -eq 'RemoveFailure') { throw 'simulated remove failure after applying' }
            if ($registrationState.Mode -in @('RemoveNull', 'RemoveNoop')) { return @{ marketplaceName = 'personal'; installedRoot = $null } }
            return @{ marketplaceName = 'personal'; installedRoot = $old }
        }
        if ($command -eq 'plugin marketplace add') {
            $requested = $commandArguments[3]
            if ($requested -eq $fixtureRoot -and $registrationState.Mode -in @('AddFailure', 'RollbackFailure')) { throw 'simulated target add failure' }
            if ($requested -eq $previousRepository -and $registrationState.Mode -eq 'RollbackFailure') { throw 'simulated rollback failure' }
            $already = $null -ne $registrationState.Root
            if ($requested -eq $fixtureRoot -and $registrationState.Mode -eq 'WrongAlreadyAdded') {
                $registrationState.Root = $previousRepository
                return @{ marketplaceName = 'personal'; installedRoot = $previousRepository; alreadyAdded = $true }
            }
            if ($requested -eq $fixtureRoot -and $registrationState.Mode -eq 'WrongList') {
                $registrationState.Root = $previousRepository
                return @{ marketplaceName = 'personal'; installedRoot = $fixtureRoot; alreadyAdded = $false }
            }
            $registrationState.Root = $requested
            return @{ marketplaceName = 'personal'; installedRoot = $requested; alreadyAdded = $already }
        }
        throw "Unexpected simulated mutation: $command"
    }.GetNewClosure()
    $planArguments = @{ Executable = 'simulated'; RepositoryRoot = $fixtureRoot; PluginPath = $pluginRoot; CommandRunner = $runner }
    $plan = Get-CaveMarketplaceRegistrationPlan @planArguments
    Assert-Equal $plan.Action 'None' 'Already-correct registration.'
    Set-CaveMarketplaceRegistration -Executable simulated -Plan $plan -CommandRunner $runner
    Assert-Equal $registrationState.Mutations.Count 0 'Correct source is not re-registered.'
    $registrationState.Root = $null
    $plan = Get-CaveMarketplaceRegistrationPlan @planArguments
    Assert-Equal $plan.Action 'Add' 'Absent registration.'
    Set-CaveMarketplaceRegistration -Executable simulated -Plan $plan -CommandRunner $runner
    Assert-Equal $registrationState.Root $fixtureRoot 'Absent source added and confirmed.'
    Assert-Equal ($registrationState.Mutations -join ',') 'plugin marketplace add' 'Absent source never removed.'
    $registrationState.Root = $previousRepository
    $registrationState.Mutations.Clear()
    Assert-Rejected { Get-CaveMarketplaceRegistrationPlan @planArguments } 'Conflicting source without explicit replacement.'
    Assert-Equal $registrationState.Mutations.Count 0 'Default conflict refusal makes no mutation.'
    $registrationState.OtherPlugin = $true
    Assert-Rejected { Get-CaveMarketplaceRegistrationPlan @planArguments -ReplaceMarketplaceSource } 'Unrelated installed plugin prevents replacement.'
    Assert-Equal $registrationState.Mutations.Count 0 'Unrelated plugin is protected before removal.'
    $registrationState.OtherPlugin = $false
    $plan = Get-CaveMarketplaceRegistrationPlan @planArguments -ReplaceMarketplaceSource
    Assert-Equal $plan.Action 'Replace' 'Validated CAVE-only replacement.'
    Set-CaveMarketplaceRegistration -Executable simulated -Plan $plan -CommandRunner $runner
    Assert-Equal $registrationState.Root $fixtureRoot 'Replacement confirms exact target.'
    Assert-Equal ($registrationState.Mutations -join ',') 'plugin marketplace remove,plugin marketplace add' 'Supported remove/add sequence.'
    $recovery = Restore-CaveMarketplaceSource -Executable simulated -Plan $plan -CommandRunner $runner
    Assert-Equal $registrationState.Root $previousRepository 'Downstream installation failure restores previous registration.'
    Assert-Equal ($recovery.StartsWith('Previous source restored:')) $true 'Downstream rollback confirms restoration.'
    $registrationState.Mode = 'RemoveNull'
    $registrationState.Mutations.Clear()
    $plan = Get-CaveMarketplaceRegistrationPlan @planArguments -ReplaceMarketplaceSource
    Set-CaveMarketplaceRegistration -Executable simulated -Plan $plan -CommandRunner $runner
    Assert-Equal $registrationState.Root $fixtureRoot 'Local removal with null managed checkout root confirms the new registration.'
    Assert-Equal ($registrationState.Mutations -join ',') 'plugin marketplace remove,plugin marketplace add' 'Null local removal receipt still requires exact source re-listing.'
    foreach ($failureMode in @('WrongAlreadyAdded', 'WrongList', 'AddFailure', 'RemoveFailure', 'RemoveNoop', 'RollbackFailure')) {
        $registrationState.Root = $previousRepository
        $registrationState.Mode = $failureMode
        $registrationState.Mutations.Clear()
        $plan = Get-CaveMarketplaceRegistrationPlan @planArguments -ReplaceMarketplaceSource
        $failure = $null
        try { Set-CaveMarketplaceRegistration -Executable simulated -Plan $plan -CommandRunner $runner }
        catch { $failure = $_.Exception }
        Assert-Equal ($null -ne $failure) $true "Failure mode $failureMode stops registration."
        Assert-Equal ($null -ne $failure.InnerException) $true "Failure mode $failureMode retains the original exception."
        if ($failureMode -eq 'RollbackFailure') {
            Assert-Equal $registrationState.Root $null 'Failed rollback leaves explicit unregistered state.'
            Assert-Equal ($failure.Message.Contains("Current source: '(unregistered)'")) $true 'Failed rollback reports remaining state.'
        }
        else { Assert-Equal $registrationState.Root $previousRepository "Failure mode $failureMode restores the previous source." }
    }
    $registrationState.Mode = ''
    $registrationState.Root = $previousRepository
    $registrationState.Mutations.Clear()
    $plan = Get-CaveMarketplaceRegistrationPlan @planArguments -ReplaceMarketplaceSource
    $registrationState.Root = $fixtureRoot
    Assert-Rejected { Set-CaveMarketplaceRegistration -Executable simulated -Plan $plan -CommandRunner $runner } 'Registration changed after reviewed preflight.'
    Assert-Equal $registrationState.Mutations.Count 0 'Changed registration is not mutated.'

    Write-Host "Plugin tooling checks passed: $script:checks" -ForegroundColor Green
}
finally {
    $env:CODEX_CLI_PATH = $originalDesktopPath
    $env:PATH = $originalSearchPath
    $canonicalTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $canonicalTestRoot.StartsWith([IO.Path]::GetFullPath($testParent) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing test cleanup outside '$testParent'."
    }
    if (Test-Path -LiteralPath $canonicalTestRoot) { Remove-Item -LiteralPath $canonicalTestRoot -Recurse -Force }
}

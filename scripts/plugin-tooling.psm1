Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-CaveJsonObject {
    param([string] $Path)
    $text = Get-Content -LiteralPath $Path -Raw
    $document = [System.Text.Json.JsonDocument]::Parse($text)
    try {
        function Assert-UniqueJsonProperties($element) {
            if ($element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($property in $element.EnumerateObject()) {
                    if (-not $names.Add($property.Name)) { throw "Duplicate JSON property '$($property.Name)' in '$Path'." }
                    Assert-UniqueJsonProperties $property.Value
                }
            }
            elseif ($element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
                foreach ($item in $element.EnumerateArray()) { Assert-UniqueJsonProperties $item }
            }
        }
        Assert-UniqueJsonProperties $document.RootElement
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "Expected a JSON object in '$Path'."
        }
    }
    finally { $document.Dispose() }
    return ConvertFrom-Json -InputObject $text -AsHashtable
}

function Assert-CaveText {
    param($Value, [string] $Field)
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "'$Field' must be a nonempty string." }
}

function Get-CavePackagePath {
    param([string] $Root, $RelativePath, [ValidateSet('Leaf', 'Container', 'OptionalLeaf')] [string] $Kind = 'Leaf')
    if ($RelativePath -isnot [string] -or -not $RelativePath.StartsWith('./') -or
        $RelativePath.Contains('\') -or $RelativePath -match '(^|/)\.\.(/|$)|:') {
        throw "Package path '$RelativePath' must start with ./ and stay inside '$Root'."
    }
    $canonicalRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
    $path = [IO.Path]::GetFullPath((Join-Path $canonicalRoot $RelativePath.Substring(2)))
    if (-not $path.StartsWith($canonicalRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package path '$RelativePath' escapes '$Root'."
    }
    # CONSTRAINT: lexical containment alone does not protect package references through junctions/symlinks.
    for ($candidate = $path; $candidate -ne $canonicalRoot; $candidate = Split-Path -Parent $candidate) {
        if (Test-Path -LiteralPath $candidate) {
            if ((Get-Item -LiteralPath $candidate -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Package reference '$RelativePath' traverses a link: $candidate"
            }
        }
    }
    if ($Kind -ne 'OptionalLeaf' -and -not (Test-Path -LiteralPath $path -PathType $Kind)) {
        throw "Required package $Kind is missing: $path"
    }
    return $path
}

function Get-CaveVersionParts {
    param($Version)
    $semantic = '(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?'
    if ($Version -isnot [string] -or $Version -cnotmatch "^(?<base>$semantic)(?:\+codex\.(?<stamp>\d{14}))?$" ) {
        throw "Unsupported CAVE version '$Version'; use SemVer with an optional +codex.<14 digits> suffix."
    }
    foreach ($identifier in (($Matches.base -split '-', 2 | Select-Object -Skip 1) -split '\.')) {
        if ($identifier -match '^0\d+$') { throw "Invalid numeric prerelease identifier in '$Version'." }
    }
    return @{ Base = $Matches.base; Stamp = $Matches['stamp'] }
}

function Assert-CaveSkill {
    param([string] $Path, [string] $Name)
    $text = Get-Content -LiteralPath $Path -Raw
    $frontmatter = [regex]::Match($text, '\A---\r?\n(?<yaml>[\s\S]*?)\r?\n---\r?\n(?<body>[\s\S]+)\z')
    if (-not $frontmatter.Success -or [string]::IsNullOrWhiteSpace($frontmatter.Groups['body'].Value)) {
        throw "Skill '$Path' needs YAML frontmatter and an instruction body."
    }
    # This package deliberately uses two plain YAML scalars. Reject unsupported syntax rather than
    # approximating YAML or depending on runtime-private plugin-creator scripts/downloaded parsers.
    $fields = @{}
    foreach ($line in $frontmatter.Groups['yaml'].Value -split '\r?\n') {
        if ($line -notmatch '^(name|description): ([^\s\[\]{},&*!|>''"%@`#][^\r\n]*)$') {
            throw "Skill '$Path' must use CAVE's name/description plain-scalar frontmatter."
        }
        $key, $value = $Matches[1], $Matches[2].Trim()
        if ($fields.ContainsKey($key) -or $value -match ':\s|\s#') { throw "Invalid or duplicate skill '$key' in '$Path'." }
        $fields[$key] = $value
    }
    if ($fields.Count -ne 2 -or $fields.name -cne $Name -or $Name -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or
        $Name.Length -gt 64 -or -not $fields.ContainsKey('description') -or $fields.description.Length -gt 1024) {
        throw "Skill '$Path' has invalid name/description metadata."
    }
}

function Assert-CaveSkillInterface {
    param([string] $Path, [string] $Name)
    $lines = @(Get-Content -LiteralPath $Path | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 4 -or $lines[0] -cne 'interface:') { throw "Unsupported CAVE skill interface metadata in '$Path'." }
    $fields = @{}
    foreach ($line in $lines[1..3]) {
        if ($line -cnotmatch '^  (display_name|short_description|default_prompt): (".*")$') {
            throw "Skill interface '$Path' must use quoted string metadata."
        }
        $key = $Matches[1]
        $value = ConvertFrom-Json -InputObject $Matches[2]
        Assert-CaveText $value "$Path.$key"
        if ($fields.ContainsKey($key)) { throw "Duplicate skill interface '$key' in '$Path'." }
        $fields[$key] = $value
    }
    if ($fields.Count -ne 3 -or -not $fields.default_prompt.Contains('$' + $Name)) {
        throw "Skill interface '$Path' must reference its own skill."
    }
}

function Test-CavePluginPackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $PluginPath, [string] $RepositoryRoot,
        [switch] $RequireBinaries, [switch] $SkipRepositoryChecks)
    $root = (Resolve-Path -LiteralPath $PluginPath).Path
    if ((Test-Path -LiteralPath (Join-Path $root 'plugin.json')) -or (Test-Path -LiteralPath (Join-Path $root 'hooks.json'))) {
        throw 'CAVE uses .codex-plugin/plugin.json and default hooks/hooks.json; alternate root manifests/hooks require an explicit package migration.'
    }
    $manifest = Read-CaveJsonObject (Get-CavePackagePath $root './.codex-plugin/plugin.json')
    if ($manifest.name -cne 'cave' -or $manifest.license -cne 'MIT') { throw 'CAVE name and MIT license metadata are required.' }
    Assert-CaveText $manifest.description 'description'
    Assert-CaveText $manifest.author.name 'author.name'
    $version = Get-CaveVersionParts $manifest.version
    if ($manifest.skills -cne './skills/' -or $manifest.mcpServers -cne './.mcp.json') {
        throw 'CAVE skills and mcpServers must point to ./skills/ and ./.mcp.json.'
    }
    if ($manifest.ContainsKey('hooks') -and $manifest.hooks -cne './hooks/hooks.json') { throw 'CAVE hooks must use hooks/hooks.json.' }
    $skillsRoot = Get-CavePackagePath $root $manifest.skills Container
    foreach ($requiredSkill in @('cave', 'init', 'open')) {
        Get-CavePackagePath $root "./skills/$requiredSkill/SKILL.md" | Out-Null
    }
    foreach ($skill in Get-ChildItem -LiteralPath $skillsRoot -Directory) {
        Assert-CaveSkill (Get-CavePackagePath $root "./skills/$($skill.Name)/SKILL.md") $skill.Name
        $interfacePath = Get-CavePackagePath $root "./skills/$($skill.Name)/agents/openai.yaml" OptionalLeaf
        if (Test-Path -LiteralPath $interfacePath) { Assert-CaveSkillInterface $interfacePath $skill.Name }
    }
    foreach ($field in @('displayName', 'shortDescription', 'longDescription', 'developerName', 'category')) {
        Assert-CaveText $manifest.interface[$field] "interface.$field"
    }
    if ($manifest.interface.brandColor -cnotmatch '^#[0-9A-Fa-f]{6}$') { throw 'interface.brandColor must be a six-digit hex color.' }
    if ($manifest.interface.defaultPrompt -isnot [array] -or $manifest.interface.defaultPrompt.Count -eq 0) { throw 'interface.defaultPrompt must be a nonempty array.' }
    foreach ($prompt in $manifest.interface.defaultPrompt) { Assert-CaveText $prompt 'interface.defaultPrompt' }
    foreach ($field in @('composerIcon', 'logo', 'logoDark')) {
        Get-CavePackagePath $root $manifest.interface[$field] | Out-Null
    }
    if ($manifest.interface.ContainsKey('screenshots')) {
        if ($manifest.interface.screenshots -isnot [array]) { throw 'interface.screenshots must be an array.' }
        foreach ($screenshot in $manifest.interface.screenshots) { Get-CavePackagePath $root $screenshot | Out-Null }
    }
    $mcp = Read-CaveJsonObject (Get-CavePackagePath $root $manifest.mcpServers)
    if ($mcp.mcpServers -isnot [Collections.IDictionary] -or $mcp.mcpServers.Count -ne 1 -or -not $mcp.mcpServers.Contains('cave')) {
        throw 'CAVE must declare exactly its own MCP server.'
    }
    $server = $mcp.mcpServers.cave
    if ($server.command -cne './server/Cave.Mcp.exe' -or $server.cwd -cne '.' -or $server.args -isnot [array] -or $server.args.Count -ne 0) {
        throw 'The CAVE MCP entry point, working directory or arguments are unexpected.'
    }
    foreach ($binary in @('Cave.Mcp.exe', 'Cave.Host.exe')) {
        $kind = if ($RequireBinaries) { 'Leaf' } else { 'OptionalLeaf' }
        Get-CavePackagePath $root "./server/$binary" $kind | Out-Null
    }
    Get-CavePackagePath $root './scripts/cave_activity_hook.py' | Out-Null
    $hooks = Read-CaveJsonObject (Get-CavePackagePath $root './hooks/hooks.json')
    $events = @('SessionStart', 'SessionEnd', 'UserPromptSubmit', 'SubagentStart', 'SubagentStop', 'PreToolUse', 'PostToolUse', 'Stop', 'Interrupt')
    if ($hooks.hooks -isnot [Collections.IDictionary]) { throw 'CAVE hooks must be an event map.' }
    foreach ($eventName in $events) {
        $groups = $hooks.hooks[$eventName]
        if ($groups -isnot [array] -or $groups.Count -eq 0) { throw "CAVE hook '$eventName' must have matcher groups." }
        foreach ($group in $groups) {
            if ($group.hooks -isnot [array] -or $group.hooks.Count -eq 0) { throw "CAVE hook '$eventName' must have handlers." }
            foreach ($handler in $group.hooks) {
                if ($handler.type -cne 'command' -or $handler.ContainsKey('timeoutSec') -or
                    ($handler.timeout -isnot [long] -and $handler.timeout -isnot [int]) -or $handler.timeout -le 0 -or $handler.timeout -gt 60) {
                    throw "CAVE hook '$eventName' needs a command and supported timeout (1-60 seconds)."
                }
                foreach ($field in @('command', 'commandWindows')) {
                    Assert-CaveText $handler[$field] "$eventName.$field"
                    if ($handler[$field] -match '%PLUGIN_ROOT%|\$env:PLUGIN_ROOT' -or
                        -not $handler[$field].Contains("os.environ.get('PLUGIN_ROOT')") -or
                        -not $handler[$field].Contains('.codex/plugins/cache/*/cave/*/scripts/cave_activity_hook.py')) {
                        throw "CAVE hook '$eventName' must resolve PLUGIN_ROOT in Python and retain versioned-runner recovery."
                    }
                }
            }
        }
    }
    foreach ($eventName in $hooks.hooks.Keys) {
        if ($eventName -cnotin $events) { throw "Unsupported CAVE hook event '$eventName'." }
    }
    if (-not $SkipRepositoryChecks) {
        $repositoryVersion = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'VERSION') -Raw).Trim()
        if ($version.Base -cne $repositoryVersion) { throw "Plugin version '$($manifest.version)' does not match repository VERSION '$repositoryVersion'." }
        $frontend = Read-CaveJsonObject (Join-Path $RepositoryRoot 'src/Cave.Ui/package.json')
        [xml] $props = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
        if ($frontend.version -cne $repositoryVersion -or $props.Project.PropertyGroup.VersionPrefix -cne $repositoryVersion) {
            throw 'Frontend and .NET versions must match repository VERSION.'
        }
    }
    return $manifest
}

function Get-CaveRepositoryMarketplace {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $RepositoryRoot, [Parameter(Mandatory)] [string] $PluginPath)
    $marketplace = Read-CaveJsonObject (Join-Path $RepositoryRoot '.agents/plugins/marketplace.json')
    if ($marketplace.name -isnot [string] -or $marketplace.name -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') { throw 'Invalid repository marketplace name.' }
    Assert-CaveText $marketplace.interface.displayName 'marketplace.interface.displayName'
    if ($marketplace.plugins -isnot [array] -or $marketplace.plugins.Count -eq 0) { throw 'Marketplace plugins must be a nonempty array.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $matchingEntries = 0
    foreach ($entry in $marketplace.plugins) {
        if ($entry.name -isnot [string] -or $entry.name -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $names.Add($entry.name)) { throw 'Invalid or duplicate marketplace plugin name.' }
        if ($entry.source.source -cne 'local') { throw 'CAVE repository marketplace entries must use local sources.' }
        $source = Get-CavePackagePath $RepositoryRoot $entry.source.path Container
        if ($entry.policy.installation -cnotin @('AVAILABLE', 'INSTALLED_BY_DEFAULT', 'NOT_AVAILABLE') -or
            $entry.policy.authentication -cnotin @('ON_INSTALL', 'ON_USE')) { throw 'Invalid marketplace install/authentication policy.' }
        Assert-CaveText $entry.category 'marketplace.category'
        $identity = Read-CaveJsonObject (Get-CavePackagePath $source './.codex-plugin/plugin.json')
        if ($identity.name -cne $entry.name) { throw 'Marketplace entry name does not match its plugin manifest.' }
        if ($entry.name -ceq 'cave' -and $source -eq (Resolve-Path -LiteralPath $PluginPath).Path) { $matchingEntries++ }
    }
    if ($matchingEntries -ne 1) { throw 'The repository marketplace must point exactly once at this CAVE plugin.' }
    return $marketplace.name
}

function Set-CavePluginCachebuster {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $PluginPath, [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow)
    $path = Get-CavePackagePath $PluginPath './.codex-plugin/plugin.json'
    $manifest = Read-CaveJsonObject $path
    $version = Get-CaveVersionParts $manifest.version
    $next = [Int64]::Parse($Now.UtcDateTime.ToString('yyyyMMddHHmmss', [Globalization.CultureInfo]::InvariantCulture))
    if ($version.Stamp -and [Int64]::Parse($version.Stamp) -ge $next) { $next = [Int64]::Parse($version.Stamp) + 1 }
    if ($next -gt 99999999999999) { throw 'CAVE cachebuster exceeded its 14-digit range.' }
    # CONSTRAINT: cache paths are version-addressed; retain monotonicity across clock skew and repeat installs.
    $manifest.version = "$($version.Base)+codex.$($next.ToString('00000000000000', [Globalization.CultureInfo]::InvariantCulture))"
    [IO.File]::WriteAllText($path, (($manifest | ConvertTo-Json -Depth 32) + "`n"), [Text.UTF8Encoding]::new($false))
    return $manifest.version
}

function Resolve-CaveCodexExecutable {
    [CmdletBinding()]
    param()
    $desktopPath = [Environment]::GetEnvironmentVariable('CODEX_CLI_PATH')
    if ($null -ne $desktopPath) {
        if ([string]::IsNullOrWhiteSpace($desktopPath) -or -not [IO.Path]::IsPathFullyQualified($desktopPath) -or
            -not (Test-Path -LiteralPath $desktopPath -PathType Leaf) -or ($IsWindows -and [IO.Path]::GetExtension($desktopPath) -ine '.exe')) {
            throw 'CODEX_CLI_PATH must select an existing absolute Codex Desktop executable; refusing PATH fallback.'
        }
        $executable = (Resolve-Path -LiteralPath $desktopPath).Path
    }
    else {
        $command = Get-Command codex -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) { throw 'No Codex backend found. Run from Codex Desktop (CODEX_CLI_PATH), or provide codex on PATH.' }
        $executable = $command.Source
    }
    $version = & $executable --version
    if ($LASTEXITCODE -ne 0 -or ($version -join "`n") -notmatch '\Acodex-cli \S+\s*\z') { throw "'$executable' did not identify itself as codex-cli." }
    return $executable
}

function Invoke-CaveCodexJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Executable, [Parameter(Mandatory)] [string[]] $Arguments, [string] $RequiredArrayProperty)
    $output = & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Codex $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    $result = ConvertFrom-Json -InputObject ($output -join "`n") -AsHashtable
    if ($result -isnot [Collections.IDictionary]) { throw 'Codex plugin command did not return a JSON object.' }
    if ($RequiredArrayProperty -and $result[$RequiredArrayProperty] -isnot [array]) {
        throw "Codex plugin response is missing the '$RequiredArrayProperty' array."
    }
    return $result
}

function Resolve-CaveMarketplaceRoot {
    param($Root)
    Assert-CaveText $Root 'marketplace.root'
    if ($IsWindows) {
        $Root = $Root.Replace('/', '\') -replace '^\\+\?\\+UNC\\+', '\\' -replace '^\\+\?\\+', ''
        $unc = $Root.StartsWith('\\')
        $Root = [regex]::Replace($Root, '\\+', '\')
        if ($unc) { $Root = '\' + $Root }
    }
    if (-not [IO.Path]::IsPathFullyQualified($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Marketplace root is not an existing absolute directory: '$Root'."
    }
    $path = (Resolve-Path -LiteralPath $Root).ProviderPath
    if ($path.StartsWith('\\?\UNC\')) { $path = '\\' + $path.Substring(8) }
    elseif ($path.StartsWith('\\?\')) { $path = $path.Substring(4) }
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($path))
}

function Invoke-CaveMarketplaceCommand {
    param([string] $Executable, [string[]] $Arguments, [scriptblock] $CommandRunner, [string] $ArrayProperty)
    $result = if ($null -ne $CommandRunner) { & $CommandRunner $Arguments }
        else { Invoke-CaveCodexJson -Executable $Executable -Arguments $Arguments }
    if ($result -isnot [Collections.IDictionary] -or ($ArrayProperty -and $result[$ArrayProperty] -isnot [array])) {
        throw "Invalid Codex marketplace response for '$($Arguments -join ' ')'."
    }
    return $result
}

function Get-CaveRegisteredMarketplaceRoot {
    param([string] $Executable, [string] $Name, [scriptblock] $CommandRunner)
    $state = Invoke-CaveMarketplaceCommand $Executable @('plugin', 'marketplace', 'list', '--json') $CommandRunner marketplaces
    $entries = @($state.marketplaces | Where-Object { $_.name -ceq $Name })
    if ($entries.Count -gt 1) { throw "Codex reports duplicate '$Name' marketplaces." }
    if ($entries.Count -eq 0) { return $null }
    return @{ Root = Resolve-CaveMarketplaceRoot $entries[0].root; Entry = $entries[0] }
}

function Get-CaveMarketplaceRegistrationPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Executable, [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $PluginPath, [switch] $ReplaceMarketplaceSource, [scriptblock] $CommandRunner)
    $name = Get-CaveRepositoryMarketplace $RepositoryRoot $PluginPath
    $target = Resolve-CaveMarketplaceRoot $RepositoryRoot
    $binding = Get-CaveRegisteredMarketplaceRoot $Executable $name $CommandRunner
    $previous = if ($null -ne $binding) { $binding.Root } else { $null }
    $action = if ($null -eq $previous) { 'Add' } elseif ($previous -eq $target) { 'None' } else { 'Replace' }
    if ($action -eq 'Replace' -and -not $ReplaceMarketplaceSource) {
        throw "Marketplace '$name' belongs to '$previous', not '$target'. No changes made. Review -ReplaceMarketplaceSource with -PreflightOnly before an explicit rebind."
    }
    if ($action -ne 'None') {
        $plugins = Invoke-CaveMarketplaceCommand $Executable @('plugin', 'list', '--json') $CommandRunner installed
        $members = @($plugins.installed | Where-Object {
            $_.marketplaceName -ceq $name -or ([string] $_.pluginId).EndsWith("@$name", [StringComparison]::Ordinal)
        })
        foreach ($member in $members) {
            if ($member.name -cne 'cave' -or $member.pluginId -cne "cave@$name") {
                throw "Marketplace '$name' contains an installed non-CAVE plugin; refusing source replacement."
            }
        }
        if ($action -eq 'Replace') {
            # The listing may omit marketplaceSource; installed plugin metadata can independently
            # prove the source is local. Never infer locality from a cached Git directory alone.
            $localEvidence = $false
            if ($binding.Entry.Contains('marketplaceSource') -and $null -ne $binding.Entry.marketplaceSource) {
                if ($binding.Entry.marketplaceSource.sourceType -cne 'local') { throw 'Only a local CAVE marketplace may be replaced.' }
                $localEvidence = $true
            }
            foreach ($member in $members) {
                if ($member.marketplaceSource.sourceType -cne 'local' -or
                    (Resolve-CaveMarketplaceRoot $member.marketplaceSource.source) -ne $previous) {
                    throw 'Installed CAVE metadata does not identify the same local marketplace source.'
                }
                $localEvidence = $true
            }
            if (-not $localEvidence) { throw 'Codex did not provide evidence that the previous marketplace is local.' }
            $previousPlugin = Join-Path $previous 'plugins/cave'
            $previousName = Get-CaveRepositoryMarketplace $previous $previousPlugin
            $previousManifest = Read-CaveJsonObject (Join-Path $previous '.agents/plugins/marketplace.json')
            if ($previousName -cne $name -or $previousManifest.plugins.Count -ne 1 -or $previousManifest.plugins[0].name -cne 'cave') {
                throw 'Only a CAVE-only marketplace source may be replaced.'
            }
            Test-CavePluginPackage -PluginPath $previousPlugin -SkipRepositoryChecks | Out-Null
        }
    }
    return [pscustomobject]@{ Name = $name; TargetRoot = $target; PreviousRoot = $previous; Action = $action; PluginPath = $PluginPath }
}

function Assert-CaveMarketplaceCommandResult {
    param($Result, [string] $Name, [string] $Root, [switch] $Add)
    # CONSTRAINT: Desktop can return installedRoot:null when removing a local source
    # because no managed Git checkout was deleted. The subsequent listing proves removal.
    if ($Result.marketplaceName -cne $Name -or -not $Result.Contains('installedRoot') -or
        (($Add -or $null -ne $Result.installedRoot) -and (Resolve-CaveMarketplaceRoot $Result.installedRoot) -ne $Root) -or
        ($Add -and $Result.alreadyAdded -isnot [bool])) {
        throw "Codex marketplace command did not confirm '$Name' at '$Root'."
    }
}

function Restore-CaveMarketplaceSource {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Executable, [Parameter(Mandatory)] $Plan, [scriptblock] $CommandRunner)
    # One bounded compensating attempt through supported commands. Never remove an unexpected
    # third-party root while trying to restore the validated previous local source.
    try {
        $current = Get-CaveRegisteredMarketplaceRoot $Executable $Plan.Name $CommandRunner
        if ($null -ne $current -and $current.Root -eq $Plan.TargetRoot) {
            $removed = Invoke-CaveMarketplaceCommand $Executable @('plugin', 'marketplace', 'remove', $Plan.Name, '--json') $CommandRunner
            Assert-CaveMarketplaceCommandResult $removed $Plan.Name $Plan.TargetRoot
        }
        $restored = Invoke-CaveMarketplaceCommand $Executable @('plugin', 'marketplace', 'add', $Plan.PreviousRoot, '--json') $CommandRunner
        Assert-CaveMarketplaceCommandResult $restored $Plan.Name $Plan.PreviousRoot -Add
        $confirmed = Get-CaveRegisteredMarketplaceRoot $Executable $Plan.Name $CommandRunner
        if ($null -eq $confirmed -or $confirmed.Root -ne $Plan.PreviousRoot) { throw 'Previous marketplace root was not restored.' }
        return "Previous source restored: '$($Plan.PreviousRoot)'."
    }
    catch {
        $recovery = "Previous-source restoration failed: $($_.Exception.Message)"
        try {
            $remaining = Get-CaveRegisteredMarketplaceRoot $Executable $Plan.Name $CommandRunner
            $remainingRoot = if ($null -eq $remaining) { '(unregistered)' } else { $remaining.Root }
            $recovery += " Current source: '$remainingRoot'."
        }
        catch { $recovery += ' Current registration could not be read.' }
        return $recovery + ' Installation was stopped; inspect codex plugin marketplace list --json.'
    }
}

function Set-CaveMarketplaceRegistration {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Executable, [Parameter(Mandatory)] $Plan, [scriptblock] $CommandRunner)
    $fresh = Get-CaveMarketplaceRegistrationPlan -Executable $Executable -RepositoryRoot $Plan.TargetRoot `
        -PluginPath $Plan.PluginPath -ReplaceMarketplaceSource:($Plan.Action -eq 'Replace') -CommandRunner $CommandRunner
    if ($fresh.Name -cne $Plan.Name -or $fresh.Action -cne $Plan.Action -or $fresh.PreviousRoot -ne $Plan.PreviousRoot) {
        throw 'Marketplace registration changed after preflight; no registration changes made. Run preflight again.'
    }
    if ($Plan.Action -eq 'None') { return }
    $removalAttempted = $false
    try {
        if ($Plan.Action -eq 'Replace') {
            $removalAttempted = $true
            $removed = Invoke-CaveMarketplaceCommand $Executable @('plugin', 'marketplace', 'remove', $Plan.Name, '--json') $CommandRunner
            Assert-CaveMarketplaceCommandResult $removed $Plan.Name $Plan.PreviousRoot
            if ($null -ne (Get-CaveRegisteredMarketplaceRoot $Executable $Plan.Name $CommandRunner)) {
                throw 'Codex marketplace list still contains the source after removal.'
            }
        }
        $added = Invoke-CaveMarketplaceCommand $Executable @('plugin', 'marketplace', 'add', $Plan.TargetRoot, '--json') $CommandRunner
        # alreadyAdded is informational, never evidence that the requested root was registered.
        $confirmed = Get-CaveRegisteredMarketplaceRoot $Executable $Plan.Name $CommandRunner
        Assert-CaveMarketplaceCommandResult $added $Plan.Name $Plan.TargetRoot -Add
        if ($null -eq $confirmed -or $confirmed.Root -ne $Plan.TargetRoot) {
            throw "Codex marketplace list did not confirm '$($Plan.Name)' at '$($Plan.TargetRoot)'."
        }
    }
    catch {
        $originalFailure = $_.Exception
        if (-not $removalAttempted) { throw }
        $recovery = Restore-CaveMarketplaceSource -Executable $Executable -Plan $Plan -CommandRunner $CommandRunner
        throw [InvalidOperationException]::new("Marketplace registration failed: $($originalFailure.Message) $recovery", $originalFailure)
    }
}

Export-ModuleMember -Function Test-CavePluginPackage, Get-CaveRepositoryMarketplace, Set-CavePluginCachebuster, Resolve-CaveCodexExecutable, Invoke-CaveCodexJson, Get-CaveMarketplaceRegistrationPlan, Set-CaveMarketplaceRegistration, Restore-CaveMarketplaceSource

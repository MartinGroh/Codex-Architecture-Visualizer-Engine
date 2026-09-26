[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PluginPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$resolvedPluginPath = (Resolve-Path -LiteralPath $PluginPath).Path
$pluginCreatorRoot = Join-Path $env:USERPROFILE '.codex\skills\.system\plugin-creator'
$validatorScript = Join-Path $pluginCreatorRoot 'scripts\validate_plugin.py'
$environmentRoot = Join-Path $repoRoot 'artifacts\tools\plugin-validator'
$validatorPython = Join-Path $environmentRoot 'Scripts\python.exe'
$hooksManifest = Join-Path $resolvedPluginPath 'hooks\hooks.json'

if (-not (Test-Path -LiteralPath $validatorScript)) {
    throw "The Codex plugin validator is missing: $validatorScript"
}


if (-not (Test-Path -LiteralPath $hooksManifest -PathType Leaf)) {
    throw "The plugin must publish Codex hooks at '$hooksManifest'."
}

if (Test-Path -LiteralPath (Join-Path $resolvedPluginPath 'hooks.json')) {
    throw 'Root-level plugin hooks.json is not auto-discovered by Codex; use hooks/hooks.json.'
}

$hookConfiguration = Get-Content -LiteralPath $hooksManifest -Raw | ConvertFrom-Json
foreach ($requiredEvent in @('SessionStart', 'SessionEnd', 'UserPromptSubmit', 'SubagentStart', 'SubagentStop', 'PreToolUse', 'PostToolUse', 'Stop', 'Interrupt')) {
    $eventConfiguration = $hookConfiguration.hooks.$requiredEvent
    if ($null -eq $eventConfiguration) {
        throw "The CAVE plugin hook manifest is missing '$requiredEvent'."
    }

    foreach ($matcherGroup in @($eventConfiguration)) {
        foreach ($handler in @($matcherGroup.hooks)) {
            if ([string]::IsNullOrWhiteSpace($handler.commandWindows)) {
                throw "The CAVE plugin hook '$requiredEvent' is missing commandWindows."
            }

            if ($handler.commandWindows -match '%PLUGIN_ROOT%' -or $handler.commandWindows -match '\$env:PLUGIN_ROOT') {
                throw "The CAVE plugin hook '$requiredEvent' uses shell-specific PLUGIN_ROOT expansion."
            }

            if ($handler.commandWindows -notmatch "os\.environ\.get\('PLUGIN_ROOT'\)") {
                throw "The CAVE plugin hook '$requiredEvent' must resolve PLUGIN_ROOT inside Python."
            }

            if (-not ([string] $handler.commandWindows).Contains(
                ".codex/plugins/cache/*/cave/*/scripts/cave_activity_hook.py")) {
                throw "The CAVE plugin hook '$requiredEvent' cannot recover after a versioned cache path is removed."
            }
        }
    }
}

if ((Get-Content -LiteralPath $hooksManifest -Raw) -match 'timeoutSec') {
    throw 'The CAVE plugin hook manifest uses unsupported timeoutSec; use timeout.'
}

if (-not (Test-Path -LiteralPath $validatorPython)) {
    python -m venv $environmentRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Creating the plugin validator environment failed with exit code $LASTEXITCODE."
    }

    & $validatorPython -m pip install --disable-pip-version-check --quiet 'PyYAML==6.0.2'
    if ($LASTEXITCODE -ne 0) {
        throw "Installing the plugin validator dependency failed with exit code $LASTEXITCODE."
    }
}

& $validatorPython $validatorScript $resolvedPluginPath
if ($LASTEXITCODE -ne 0) {
    throw "Plugin validation failed with exit code $LASTEXITCODE."
}

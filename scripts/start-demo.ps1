[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int] $Port = 5096,

    [switch] $SkipBuild,

    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$demoOutputRoot = Join-Path $repoRoot 'output\demo'
$demoWorkspaceRoot = Join-Path $demoOutputRoot 'CAVE-Demo-Workspace'
$demoCatalogRoot = Join-Path $demoOutputRoot 'catalog'
$demoLogRoot = Join-Path $demoOutputRoot 'logs'
$codeGraphRoot = Join-Path $demoWorkspaceRoot '.codegraph'
$baseUri = "http://127.0.0.1:$Port"

foreach ($path in @($demoWorkspaceRoot, $demoCatalogRoot, $demoLogRoot, $codeGraphRoot)) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

# The machine catalog intentionally lists only initialized workspaces. Demo mode owns no CodeGraph
# process, so this empty marker makes the generated demo workspace explicit without indexing user code.
$codeGraphMarker = Join-Path $codeGraphRoot 'codegraph.db'
if (-not (Test-Path -LiteralPath $codeGraphMarker)) {
    New-Item -ItemType File -Path $codeGraphMarker | Out-Null
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
    if (-not $SkipBuild) {
        Invoke-CheckedNative { npm --prefix src/Cave.Ui ci } 'Frontend dependency restore'
        Invoke-CheckedNative { npm --prefix src/Cave.Ui run build } 'Frontend build'
        Invoke-CheckedNative { dotnet build CAVE.slnx } '.NET build'
    }

    $standardOutputPath = Join-Path $demoLogRoot 'host.stdout.log'
    $standardErrorPath = Join-Path $demoLogRoot 'host.stderr.log'
    $hostProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @(
            'run',
            '--project', 'src/Cave.Host',
            '--no-build',
            '--',
            '--urls', $baseUri
        ) `
        -Environment @{
            'Cave__DemoMode' = 'true'
            'Cave__WorkspaceRoot' = $demoWorkspaceRoot
            'Cave__WorkspaceCatalogRoot' = $demoCatalogRoot
        } `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $standardOutputPath `
        -RedirectStandardError $standardErrorPath `
        -PassThru

    try {
        $ready = $false
        foreach ($attempt in 1..80) {
            if ($hostProcess.HasExited) {
                $errorText = if (Test-Path -LiteralPath $standardErrorPath) {
                    Get-Content -LiteralPath $standardErrorPath -Raw
                }
                else {
                    'No host error log was produced.'
                }
                throw "CAVE demo host exited before becoming ready. $errorText"
            }

            try {
                $health = Invoke-RestMethod -Uri "$baseUri/health" -TimeoutSec 2
                if (([string] $health).Trim() -eq 'Healthy') {
                    $ready = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }

        if (-not $ready) {
            throw "CAVE demo did not become healthy at $baseUri."
        }

        $catalog = Invoke-RestMethod -Uri "$baseUri/api/workspaces" -TimeoutSec 5
        $workspace = $catalog.workspaces | Where-Object {
            $_.rootPath -eq $demoWorkspaceRoot -and $_.isAvailable
        } | Select-Object -First 1
        if ($null -eq $workspace) {
            throw 'The generated CAVE demo workspace was not registered.'
        }

        $workspaceId = [uri]::EscapeDataString([string] $workspace.workspaceId)
        $demoUri = "$baseUri/?workspace=$workspaceId"
        Write-Host "CAVE demo is running at $demoUri"
        Write-Host 'All graph, Git, activity, conversation, and usage values are synthetic.'
        Write-Host 'Press Ctrl+C to stop the demo.'

        if (-not $NoBrowser) {
            Start-Process -FilePath $demoUri | Out-Null
        }

        Wait-Process -Id $hostProcess.Id
    }
    finally {
        if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
            Stop-Process -Id $hostProcess.Id
            $hostProcess.WaitForExit(5000) | Out-Null
        }
    }
}
finally {
    Pop-Location
}

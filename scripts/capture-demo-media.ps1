[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int] $Port = 5096,

    [switch] $SkipBuild,

    [switch] $SkipVideo
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$rawOutput = Join-Path $repoRoot 'output\playwright'
$mediaOutput = Join-Path $repoRoot 'docs\media'
$demoOutput = Join-Path $repoRoot 'output\demo'
$demoWorkspace = Join-Path $demoOutput 'CAVE-Demo-Workspace'
$demoCatalog = Join-Path $demoOutput 'catalog'
$demoLogs = Join-Path $demoOutput 'logs'
$codeGraphRoot = Join-Path $demoWorkspace '.codegraph'
$baseUri = "http://127.0.0.1:$Port"
$sessionName = "cave-media-$PID"
$hostProcess = $null

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)] [scriptblock] $Command,
        [Parameter(Mandatory)] [string] $Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Playwright {
    param(
        [Parameter(Mandatory)] [string[]] $CliArguments,
        [Parameter(Mandatory)] [string] $Description
    )

    & npx --yes --package '@playwright/cli' playwright-cli "-s=$sessionName" @CliArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

foreach ($requiredCommand in @('dotnet', 'npm', 'npx')) {
    if ($null -eq (Get-Command $requiredCommand -ErrorAction SilentlyContinue)) {
        throw "Required command '$requiredCommand' is not installed."
    }
}
if (-not $SkipVideo -and $null -eq (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw "ffmpeg is required for MP4 walkthroughs. Install it or pass -SkipVideo."
}

foreach ($path in @($rawOutput, $mediaOutput, $demoWorkspace, $demoCatalog, $demoLogs, $codeGraphRoot)) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}
$codeGraphMarker = Join-Path $codeGraphRoot 'codegraph.db'
if (-not (Test-Path -LiteralPath $codeGraphMarker)) {
    New-Item -ItemType File -Path $codeGraphMarker | Out-Null
}

Push-Location $repoRoot
try {
    if (-not $SkipBuild) {
        Invoke-CheckedNative { npm --prefix src/Cave.Ui ci } 'Frontend dependency restore'
        Invoke-CheckedNative { npm --prefix src/Cave.Ui run build } 'Frontend build'
        Invoke-CheckedNative { dotnet build CAVE.slnx } '.NET build'
    }

    $hostProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', 'src/Cave.Host', '--no-build', '--', '--urls', $baseUri) `
        -Environment @{
            'Cave__DemoMode' = 'true'
            'Cave__WorkspaceRoot' = $demoWorkspace
            'Cave__WorkspaceCatalogRoot' = $demoCatalog
        } `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $demoLogs 'capture.stdout.log') `
        -RedirectStandardError (Join-Path $demoLogs 'capture.stderr.log') `
        -PassThru

    $ready = $false
    foreach ($attempt in 1..80) {
        if ($hostProcess.HasExited) {
            throw "Demo host exited before capture (exit code $($hostProcess.ExitCode))."
        }
        try {
            if (([string] (Invoke-RestMethod -Uri "$baseUri/health" -TimeoutSec 2)).Trim() -eq 'Healthy') {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $ready) {
        throw "Demo host did not become healthy at $baseUri."
    }

    $catalog = Invoke-RestMethod -Uri "$baseUri/api/workspaces" -TimeoutSec 5
    $workspace = $catalog.workspaces | Where-Object {
        $_.rootPath -eq $demoWorkspace -and $_.isAvailable
    } | Select-Object -First 1
    if ($null -eq $workspace) {
        throw 'Generated demo workspace was not registered.'
    }
    $demoUri = "$baseUri/?workspace=$([uri]::EscapeDataString([string] $workspace.workspaceId))"

    Invoke-Playwright -CliArguments @('open', $demoUri) -Description 'Open demo in Playwright'
    Invoke-Playwright -CliArguments @('resize', '1600', '900') -Description 'Set capture viewport'

    $prepareLive = "async page => { await page.getByLabel(/Color theme/).selectOption({label:'Light'}); await page.getByLabel('Graph mode').getByRole('button',{name:'Live activity',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); const close=page.getByRole('button',{name:'Close selection details'}); if(await close.count()) await close.click(); await page.waitForTimeout(1500); }"
    Invoke-Playwright -CliArguments @('run-code', $prepareLive) -Description 'Prepare live demo view'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/hero-live.png') -Description 'Capture live hero'

    $systemView = "async page => { await page.getByLabel('Graph mode').getByRole('button',{name:'Architecture',exact:true}).click(); await page.getByRole('button',{name:'System',exact:true}).click(); await page.waitForTimeout(1800); }"
    Invoke-Playwright -CliArguments @('run-code', $systemView) -Description 'Open system projection'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/architecture-system.png') -Description 'Capture system projection'

    $projectView = "async page => { await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1800); }"
    Invoke-Playwright -CliArguments @('run-code', $projectView) -Description 'Open project projection'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/architecture-projects.png') -Description 'Capture project projection'

    $classView = "async page => { await page.getByRole('button',{name:'Classes',exact:true}).click(); await page.waitForTimeout(2000); }"
    Invoke-Playwright -CliArguments @('run-code', $classView) -Description 'Open class projection'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/architecture-classes.png') -Description 'Capture class projection'

    $changeView = "async page => { await page.getByLabel('Graph mode').getByRole('button',{name:'Changes',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1800); }"
    Invoke-Playwright -CliArguments @('run-code', $changeView) -Description 'Open change projection'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/git-changes.png') -Description 'Capture Git changes'

    $openConversation = "async page => { await page.getByRole('button',{name:'Open Codex conversation'}).click(); await page.waitForTimeout(400); }"
    Invoke-Playwright -CliArguments @('run-code', $openConversation) -Description 'Open demo conversation'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/conversation.png') -Description 'Capture conversation'
    $openInfo = "async page => { await page.getByRole('button',{name:'Close conversation'}).click(); await page.getByRole('button',{name:'Open CAVE information'}).click(); await page.waitForTimeout(400); }"
    Invoke-Playwright -CliArguments @('run-code', $openInfo) -Description 'Open demo information'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/info.png') -Description 'Capture information'

    $darkView = "async page => { await page.getByRole('button',{name:'Close information'}).click(); await page.getByLabel(/Color theme/).selectOption({label:'Dark'}); await page.getByLabel('Graph mode').getByRole('button',{name:'Live activity',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1800); }"
    Invoke-Playwright -CliArguments @('run-code', $darkView) -Description 'Open dark live view'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/dark-live.png') -Description 'Capture dark view'

    $detailView = "async page => { await page.getByLabel(/Color theme/).selectOption({label:'Light'}); await page.getByLabel('Graph mode').getByRole('button',{name:'Architecture',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1500); await page.getByRole('group',{name:'Project: Atlas.Infrastructure'}).click(); await page.waitForTimeout(400); }"
    Invoke-Playwright -CliArguments @('run-code', $detailView) -Description 'Open node details'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/node-details.png') -Description 'Capture node details'

    $mobileView = "async page => { const close=page.getByRole('button',{name:'Close selection details'}); if(await close.count()) await close.click(); await page.setViewportSize({width:430,height:900}); await page.waitForTimeout(500); }"
    Invoke-Playwright -CliArguments @('run-code', $mobileView) -Description 'Prepare narrow viewport'
    Invoke-Playwright -CliArguments @('screenshot', '--filename', 'output/playwright/mobile.png') -Description 'Capture narrow viewport'
    Invoke-Playwright -CliArguments @('resize', '1600', '900') -Description 'Restore capture viewport'

    if (-not $SkipVideo) {
        Invoke-Playwright -CliArguments @('run-code', $prepareLive) -Description 'Prepare overview recording'
        Invoke-Playwright -CliArguments @('video-start', 'output/playwright/cave-overview.webm', '--size', '1600x900') -Description 'Start overview recording'
        $overview = "async page => { await page.waitForTimeout(1000); await page.getByLabel('Graph mode').getByRole('button',{name:'Architecture',exact:true}).click(); await page.getByRole('button',{name:'System',exact:true}).click(); await page.waitForTimeout(1500); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1500); await page.getByRole('button',{name:'Classes',exact:true}).click(); await page.waitForTimeout(1700); await page.getByLabel('Graph mode').getByRole('button',{name:'Changes',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1500); await page.getByRole('button',{name:'Open Codex conversation'}).click(); await page.waitForTimeout(1300); await page.getByRole('button',{name:'Close conversation'}).click(); await page.getByRole('button',{name:'Open CAVE information'}).click(); await page.waitForTimeout(1300); await page.getByRole('button',{name:'Close information'}).click(); await page.getByLabel(/Color theme/).selectOption({label:'Dark'}); await page.getByLabel('Graph mode').getByRole('button',{name:'Live activity',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1600); }"
        Invoke-Playwright -CliArguments @('run-code', $overview) -Description 'Record overview interactions'
        Invoke-Playwright -CliArguments @('video-stop') -Description 'Stop overview recording'

        Invoke-Playwright -CliArguments @('run-code', $prepareLive) -Description 'Prepare drill-down recording'
        $architectureProjects = "async page => { await page.getByLabel('Graph mode').getByRole('button',{name:'Architecture',exact:true}).click(); await page.getByRole('button',{name:'Projects',exact:true}).click(); await page.waitForTimeout(1400); }"
        Invoke-Playwright -CliArguments @('run-code', $architectureProjects) -Description 'Open architecture for drill-down'
        Invoke-Playwright -CliArguments @('video-start', 'output/playwright/cave-drilldown.webm', '--size', '1600x900') -Description 'Start drill-down recording'
        $drillDown = "async page => { await page.waitForTimeout(900); await page.getByRole('group',{name:'Project: Atlas.Application'}).click(); await page.waitForTimeout(1500); await page.getByRole('button',{name:'Close selection details'}).click(); await page.waitForTimeout(600); await page.getByRole('button',{name:'Expand Atlas.Application namespaces'}).click(); await page.waitForTimeout(1700); await page.getByRole('button',{name:'Fit View'}).click(); await page.waitForTimeout(1100); await page.getByRole('button',{name:'Classes',exact:true}).click(); await page.waitForTimeout(1700); await page.getByRole('button',{name:'Fit View'}).click(); await page.waitForTimeout(1100); }"
        Invoke-Playwright -CliArguments @('run-code', $drillDown) -Description 'Record architecture drill-down'
        Invoke-Playwright -CliArguments @('video-stop') -Description 'Stop drill-down recording'
    }

    $imageNames = @(
        'hero-live.png', 'architecture-system.png', 'architecture-projects.png',
        'architecture-classes.png', 'git-changes.png', 'conversation.png', 'info.png',
        'dark-live.png', 'node-details.png', 'mobile.png'
    )
    foreach ($imageName in $imageNames) {
        Copy-Item -LiteralPath (Join-Path $rawOutput $imageName) -Destination (Join-Path $mediaOutput $imageName) -Force
    }

    if (-not $SkipVideo) {
        Invoke-CheckedNative {
            ffmpeg -y -loglevel warning -i output/playwright/cave-overview.webm `
                -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -movflags +faststart -an `
                docs/media/cave-overview.mp4
        } 'Convert overview video'
        Invoke-CheckedNative {
            ffmpeg -y -loglevel warning -i output/playwright/cave-drilldown.webm `
                -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -movflags +faststart -an `
                docs/media/cave-drilldown.mp4
        } 'Convert drill-down video'
    }

    Write-Host "Synthetic demo media captured in $mediaOutput" -ForegroundColor Green
}
finally {
    try {
        & npx --yes --package '@playwright/cli' playwright-cli "-s=$sessionName" close 2>$null | Out-Null
    }
    catch {
        # The browser may not have opened; host cleanup remains mandatory.
    }
    if ($null -ne $hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id
        $hostProcess.WaitForExit(5000) | Out-Null
    }
    Pop-Location
}

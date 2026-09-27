[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoCandidate = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$artifactRoot = if (Test-Path -LiteralPath (Join-Path $repoCandidate 'CAVE.slnx')) {
    $repoCandidate
} else {
    Split-Path -Parent $PSScriptRoot
}
$buildDirectory = Join-Path $artifactRoot 'output/agent-flow-light-native'
$null = New-Item -ItemType Directory -Path $buildDirectory -Force
$executable = Join-Path $buildDirectory $(if ($IsWindows) { 'test_agent_flow.exe' } else { 'test_agent_flow' })
$cSources = @('cave_agent_flow.c', 'poll_example.c', 'tests/test_agent_flow.c') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$cppSource = Join-Path $PSScriptRoot 'tests/cpp_smoke.cpp'
$cl = Get-Command cl -ErrorAction SilentlyContinue
$cc = Get-Command gcc, clang -ErrorAction SilentlyContinue | Select-Object -First 1
if ($cl -or ($IsWindows -and -not $cc)) {
    $environmentScript = $null
    if (-not $cl) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) { throw 'No C compiler found. Install/select a C/C++ toolchain and rerun; this script does not install one.' }
        $installations = & $vswhere -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($LASTEXITCODE -ne 0) { throw 'Visual Studio toolchain discovery failed.' }
        foreach ($installation in $installations) {
            $candidate = Join-Path $installation 'VC/Auxiliary/Build/vcvarsall.bat'
            if (Test-Path -LiteralPath $candidate) { $environmentScript = $candidate; break }
        }
        if (-not $environmentScript) { throw 'No complete MSVC host toolchain found; no compiler was installed or machine settings changed.' }
    }
    foreach ($path in @($PSScriptRoot, $buildDirectory, $environmentScript)) {
        if ($path -and $path -match '["%&|<>^!\r\n]') { throw 'The MSVC test runner cannot quote this path safely.' }
        if ($path -and $path -match '[^\x00-\x7F]') { throw 'The MSVC batch test runner requires ASCII paths. Move the extracted pack to an ASCII path or use GCC/Clang; no path was rewritten.' }
    }
    $lines = @('@echo off')
    if ($environmentScript) { $lines += "call `"$environmentScript`" x64 >nul"; $lines += 'if errorlevel 1 exit /b 1' }
    $lines += "pushd `"$buildDirectory`""
    $lines += "cl /nologo /std:c++17 /EHsc /W4 /WX /c /I`"$PSScriptRoot`" `"$cppSource`" /Focpp_smoke.obj"
    $lines += 'if errorlevel 1 exit /b 1'
    $quotedSources = ($cSources | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $lines += "cl /nologo /std:c11 /W4 /WX /D_CRT_SECURE_NO_WARNINGS /c /TC /I`"$PSScriptRoot`" $quotedSources"
    $lines += 'if errorlevel 1 exit /b 1'
    $lines += 'cl /nologo cave_agent_flow.obj poll_example.obj test_agent_flow.obj cpp_smoke.obj /Fetest_agent_flow.exe'
    $lines += 'exit /b %errorlevel%'
    $driver = Join-Path $buildDirectory 'compile-msvc.cmd'
    [IO.File]::WriteAllText($driver, ($lines -join "`r`n") + "`r`n", [Text.Encoding]::ASCII)
    Write-Host 'Compiling portable example with installed MSVC (/W4 /WX).'
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $env:ComSpec
    $start.Arguments = '/d /s /c ""' + $driver + '""'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    Write-Host $stdout.GetAwaiter().GetResult()
    Write-Host $stderr.GetAwaiter().GetResult()
    $compileExit = $process.ExitCode
    $process.Dispose()
    if ($compileExit -ne 0) { throw 'Native C/C++ warning-gated build failed.' }
} elseif ($cc) {
    $cxxName = if ($cc.Name -like 'gcc*') { 'g++' } else { 'clang++' }
    $cxx = Get-Command $cxxName -ErrorAction Stop
    $cppObject = Join-Path $buildDirectory 'cpp_smoke.o'
    Write-Host "Compiling portable example with $($cc.Name) (-Wall -Wextra -Werror -pedantic)."
    & $cxx.Source '-std=c++17' '-Wall' '-Wextra' '-Werror' '-pedantic' "-I$PSScriptRoot" '-c' $cppSource '-o' $cppObject
    if ($LASTEXITCODE -ne 0) { throw 'C++ API compile failed.' }
    & $cc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-pedantic' "-I$PSScriptRoot" @cSources $cppObject '-o' $executable
    if ($LASTEXITCODE -ne 0) { throw 'Native C/C++ warning-gated build failed.' }
} else { throw 'No C compiler found. Select GCC, Clang, or an installed MSVC host toolchain; this script does not install one.' }

Push-Location -LiteralPath $PSScriptRoot
try {
    $arguments = @('tests/fixture.json')
    $packFixture = Join-Path $PSScriptRoot '../example.json'
    if (-not (Test-Path -LiteralPath $packFixture)) { throw 'The pack-root example.json contract fixture is missing.' }
    $arguments += $packFixture
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Native parser/polling tests failed.' }
} finally { Pop-Location }

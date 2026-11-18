[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('cave-audit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'audit-public-readiness.ps1') -Destination (Join-Path $fixture 'scripts')
$pwsh = (Get-Process -Id $PID).Path
function Invoke-Git {
    & git @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Git fixture command failed: $args" }
}
function Assert-Audit {
    param([int] $Expected, [string[]] $AuditArguments, [string] $Label, [string[]] $ExpectedRules = @())
    $output = & $pwsh -NoProfile -File ./scripts/audit-public-readiness.ps1 @AuditArguments 2>&1
    if ($LASTEXITCODE -ne $Expected) { throw "$Label expected exit $Expected but got $LASTEXITCODE. $output" }
    foreach ($rule in $ExpectedRules) {
        if (($output -join "`n") -notmatch [regex]::Escape($rule)) { throw "$Label did not report expected rule: $rule" }
    }
}
Push-Location $fixture
try {
    Invoke-Git init -b main
    Invoke-Git config user.name 'Audit fixture'
    Invoke-Git config user.email 'fixture@users.noreply.github.com'
    Set-Content clean.txt 'Public fixture'
    Invoke-Git add .
    Invoke-Git commit -m 'Clean root'
    Invoke-Git checkout -b unrelated
    # Assemble synthetic detector input; never place real credentials in fixtures.
    Set-Content evidence.txt (('pass' + 'word') + '=' + 'syntheticfixturevalue')
    Invoke-Git -c user.email=fixture@example.invalid commit --allow-empty -m 'Private identity fixture'
    Invoke-Git add evidence.txt
    Invoke-Git commit -m 'Synthetic content fixture'
    Invoke-Git checkout main
    Assert-Audit 0 @('-IncludeHistory', '-HistoryScope', 'Head') 'Unrelated branch is not HEAD ancestry'
    Assert-Audit 1 @('-IncludeHistory', '-HistoryScope', 'All') 'All sees unrelated content and identities' @('candidate-secret-assignment', 'non-noreply-author-email')
    Assert-Audit 1 @('-IncludeHistory') 'Default remains All'
    Invoke-Git merge unrelated --no-ff -m 'Include fixture ancestry'
    Invoke-Git rm evidence.txt
    Invoke-Git commit -m 'Clean current tree'
    Assert-Audit 0 @() 'Current tree is clean'
    Assert-Audit 1 @('-IncludeHistory', '-HistoryScope', 'Head') 'HEAD includes removed content and merge ancestry' @('candidate-secret-assignment', 'non-noreply-author-email')
    Assert-Audit 1 @('-IncludeHistory', '-HistoryScope', 'Invalid') 'Invalid scope fails closed'
    Write-Host 'Public-readiness scope tests passed.'
}
finally {
    Pop-Location
    # Only remove the unique fixture directory created above inside the temp root.
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped temp root.' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}

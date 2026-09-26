[CmdletBinding()]
param(
    [string] $Repository,
    [string] $Branch = 'main',
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Repository)) {
    $Repository = (& gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Repository)) {
        throw 'Pass -Repository owner/name or authenticate the GitHub CLI for this repository.'
    }
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Invalid repository name '$Repository'."
}

$protection = [ordered] @{
    required_status_checks = [ordered] @{
        strict = $true
        contexts = @(
            'Build and test',
            'Repository audit',
            'Gitleaks'
        )
    }
    enforce_admins = $true
    required_pull_request_reviews = [ordered] @{
        dismissal_restrictions = [ordered] @{ users = @(); teams = @(); apps = @() }
        dismiss_stale_reviews = $true
        require_code_owner_reviews = $true
        require_last_push_approval = $true
        required_approving_review_count = 1
    }
    restrictions = $null
    required_linear_history = $true
    allow_force_pushes = $false
    allow_deletions = $false
    block_creations = $false
    required_conversation_resolution = $true
    lock_branch = $false
    allow_fork_syncing = $true
}
$mergeSettings = [ordered] @{
    allow_squash_merge = $true
    allow_merge_commit = $false
    allow_rebase_merge = $false
    allow_auto_merge = $true
    delete_branch_on_merge = $true
}

Write-Host "Branch-protection plan for $Repository branch $Branch"
$protection | ConvertTo-Json -Depth 10
Write-Host 'Repository merge settings'
$mergeSettings | ConvertTo-Json -Depth 5
if (-not $Apply) {
    Write-Host 'Preview only. Pass -Apply with repository-owner authority to change GitHub.'
    exit 0
}

$protectionInput = [IO.Path]::GetTempFileName()
$settingsInput = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($protectionInput, ($protection | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($settingsInput, ($mergeSettings | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))

    & gh api --method PUT "repos/$Repository/branches/$Branch/protection" --input $protectionInput | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub rejected the branch-protection policy. Check repository plan, visibility, and admin permission.'
    }
    & gh api --method PATCH "repos/$Repository" --input $settingsInput | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Branch protection was applied, but repository merge settings could not be updated.'
    }

    $verified = & gh api "repos/$Repository/branches/$Branch/protection"
    if ($LASTEXITCODE -ne 0) {
        throw 'Protection was applied but could not be read back for verification.'
    }
    Write-Host "Branch protection applied and read back for $($Repository):$Branch." -ForegroundColor Green
    $verified
}
finally {
    Remove-Item -LiteralPath $protectionInput, $settingsInput -Force -ErrorAction SilentlyContinue
}

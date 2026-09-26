[CmdletBinding()]
param(
    [switch] $IncludeHistory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$findings = [System.Collections.Generic.List[object]]::new()
$textExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        '.cs', '.csproj', '.css', '.editorconfig', '.gitignore', '.html', '.js', '.json',
        '.jsx', '.md', '.mjs', '.props', '.ps1', '.psd1', '.psm1', '.py', '.slnx',
        '.targets', '.toml', '.ts', '.tsx', '.txt', '.xml', '.yaml', '.yml'
    ),
    [System.StringComparer]::OrdinalIgnoreCase
)

function Add-Finding {
    param(
        [Parameter(Mandatory)] [string] $Scope,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $Line,
        [Parameter(Mandatory)] [string] $Rule
    )

    $findings.Add([pscustomobject] @{
        Scope = $Scope
        Path = $Path
        Line = $Line
        Rule = $Rule
    })
}

function Test-TextFile {
    param([Parameter(Mandatory)] [string] $Path)

    $extension = [IO.Path]::GetExtension($Path)
    return $textExtensions.Contains($extension) -or [string]::IsNullOrWhiteSpace($extension)
}

function Test-ContentRule {
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $Lines,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $Rule
    )

    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex += 1) {
        if ($Lines[$lineIndex] -match $Pattern) {
            Add-Finding -Scope 'tree' -Path $RelativePath -Line ($lineIndex + 1) -Rule $Rule
        }
    }
}

Push-Location $repoRoot
try {
    $trackedAndCandidateFiles = @(& git ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate the repository with git.'
    }

    $sensitiveFilePattern = '(?i)(^|/)(\.env($|\.)|id_(rsa|dsa|ecdsa|ed25519)(\.|$)|[^/]+\.(pfx|p12|pem|key|jks|keystore))$'
    $generatedStatePattern = '(?i)(^|/)(\.cave|\.codegraph|\.impeccable)(/|$)|(^|/)plugins/cave/server(/|$)'
    # Keep the pinned, deliberately generic C:\Code\Example cross-language test fixture valid.
    $absoluteDeveloperPathPattern = '(?i)\b[A-Z]:\\(?:Users\\|Code\\(?!Example(?:\\|\W|$)))[^\s"''<>]+'
    $secretAssignmentPattern = '(?i)\b(api[_-]?key|client[_-]?secret|access[_-]?token|password)\s*[:=]\s*["'']?[A-Za-z0-9_./+=-]{8,}'
    $privateKeyPattern = '-----BEGIN [A-Z ]*PRIVATE KEY-----'
    $organizationTerms = @(
        ('Hammer' + 'tech'),
        ('Hammer' + 'Node'),
        ('Hammer' + 'Pi'),
        ('big' + 'boy')
    )

    foreach ($relativePath in $trackedAndCandidateFiles) {
        $normalizedPath = $relativePath.Replace('\', '/')
        if ($normalizedPath -match $sensitiveFilePattern) {
            Add-Finding -Scope 'tree' -Path $normalizedPath -Line 0 -Rule 'sensitive-file-name'
        }
        if ($normalizedPath -match $generatedStatePattern) {
            Add-Finding -Scope 'tree' -Path $normalizedPath -Line 0 -Rule 'generated-private-state'
        }

        if (-not (Test-TextFile -Path $normalizedPath)) {
            continue
        }

        $absolutePath = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
            continue
        }

        $lines = @(Get-Content -LiteralPath $absolutePath)
        Test-ContentRule -RelativePath $normalizedPath -Lines $lines -Pattern $absoluteDeveloperPathPattern -Rule 'developer-absolute-path'
        foreach ($organizationTerm in $organizationTerms) {
            Test-ContentRule `
                -RelativePath $normalizedPath `
                -Lines $lines `
                -Pattern ([regex]::Escape($organizationTerm)) `
                -Rule 'organization-specific-term'
        }

        # This script necessarily defines the secret detectors. Scan all other text files for values.
        if ($normalizedPath -ne 'scripts/audit-public-readiness.ps1') {
            Test-ContentRule -RelativePath $normalizedPath -Lines $lines -Pattern $secretAssignmentPattern -Rule 'candidate-secret-assignment'
            Test-ContentRule -RelativePath $normalizedPath -Lines $lines -Pattern $privateKeyPattern -Rule 'private-key-material'
        }
    }

    if ($IncludeHistory) {
        $historyPatterns = @(
            [pscustomobject] @{
                Rule = 'developer-absolute-path'
                GitPattern = '[A-Za-z]:\\(Users|Code)\\'
                ValidationPattern = $absoluteDeveloperPathPattern
            },
            [pscustomobject] @{
                Rule = 'candidate-secret-assignment'
                GitPattern = '(api[_-]?key|client[_-]?secret|access[_-]?token|password).{0,20}[:=]'
                ValidationPattern = $secretAssignmentPattern
            },
            [pscustomobject] @{
                Rule = 'private-key-material'
                GitPattern = '-----BEGIN [A-Z ]*PRIVATE KEY-----'
                ValidationPattern = $privateKeyPattern
            }
        )
        foreach ($organizationTerm in $organizationTerms) {
            $historyPatterns += [pscustomobject] @{
                Rule = 'organization-specific-term'
                GitPattern = $organizationTerm
                ValidationPattern = [regex]::Escape($organizationTerm)
            }
        }

        $revisions = @(& git rev-list --all)
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to enumerate Git history.'
        }

        # Public source uses GitHub noreply identities. A later clean commit cannot erase
        # private author/committer addresses from an ancestor's raw commit object.
        $identityRecords = @(& git log --all '--format=%H%x09%ae%x09%ce')
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to inspect Git commit identities.'
        }
        $publicIdentityPattern = '^[^@\s]+@users\.noreply\.github\.com$|^noreply@github\.com$'
        foreach ($identityRecord in $identityRecords) {
            $identityParts = $identityRecord -split "`t", 3
            if ($identityParts.Count -ne 3) {
                throw 'Malformed Git commit identity metadata.'
            }
            $identityScope = $identityParts[0].Substring(0, 12)
            if ($identityParts[1] -notmatch $publicIdentityPattern) {
                Add-Finding -Scope $identityScope -Path '[commit metadata]' -Line 0 -Rule 'non-noreply-author-email'
            }
            if ($identityParts[2] -notmatch $publicIdentityPattern) {
                Add-Finding -Scope $identityScope -Path '[commit metadata]' -Line 0 -Rule 'non-noreply-committer-email'
            }
        }

        foreach ($revision in $revisions) {
            foreach ($historyPattern in $historyPatterns) {
                $matches = @(& git grep -I -n -E -e $historyPattern.GitPattern $revision -- . 2>$null)
                if ($LASTEXITCODE -gt 1) {
                    throw 'Unable to scan a Git revision.'
                }
                foreach ($matchLine in $matches) {
                    $metadata = [regex]::Match(
                        $matchLine,
                        '^(?<revision>[0-9a-f]+):(?<path>.+?):(?<line>\d+):'
                    )
                    $matchedContent = if ($metadata.Success) {
                        $matchLine.Substring($metadata.Index + $metadata.Length)
                    }
                    else {
                        ''
                    }
                    if ($metadata.Success -and $matchedContent -match $historyPattern.ValidationPattern) {
                        Add-Finding `
                            -Scope $metadata.Groups['revision'].Value.Substring(0, 12) `
                            -Path $metadata.Groups['path'].Value `
                            -Line ([int] $metadata.Groups['line'].Value) `
                            -Rule $historyPattern.Rule
                    }
                }
            }
        }
    }

    if ($findings.Count -gt 0) {
        Write-Host 'Public-readiness audit failed. Values are suppressed; inspect each location locally.' -ForegroundColor Red
        $findings `
            | Sort-Object Scope, Path, Line, Rule -Unique `
            | Format-Table Scope, Path, Line, Rule -AutoSize
        exit 1
    }

    $scopeDescription = if ($IncludeHistory) { 'current tree and Git history' } else { 'current tree' }
    Write-Host "Public-readiness audit passed for the $scopeDescription." -ForegroundColor Green
    # Git grep returns 1 for no matches; GitHub's PowerShell wrapper propagates it
    # unless the successful audit explicitly terminates with a zero exit code.
    exit 0
}
finally {
    Pop-Location
}

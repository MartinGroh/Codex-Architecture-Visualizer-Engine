# Branch Protection and Merge Policy

The protected branch is `main`.

## Required policy

- Changes enter through pull requests; direct pushes are blocked.
- At least one approving review is required.
- Stale approvals are dismissed when the diff changes.
- CODEOWNERS review is required where an owner is named.
- The most recent push must be approved by someone other than its author when the platform supports it.
- Required check contexts: `Build and test`, `Repository audit`, and `Gitleaks`, verified against GitHub's check-run API. The UI groups those jobs under CI, Public readiness, and Secret scan respectively; workflow-name prefixes are not part of their API context names.
- All review conversations must be resolved.
- Linear history is required; squash merge is the normal merge strategy.
- Force pushes and branch deletion are blocked.
- Administrators follow the same rules except for a documented emergency.

Preview the exact API policy without changing GitHub:

```powershell
pwsh ./scripts/configure-branch-protection.ps1
```

Apply it only with repository-owner authority:

```powershell
pwsh ./scripts/configure-branch-protection.ps1 -Repository MartinGroh/Codex-Architecture-Visualizer-Engine -Branch main -Apply
```

## Current private-repository limitation

The GitHub API currently reports that branch protection for this private repository requires an upgraded plan or public visibility. The repository must remain private during preparation, so the configuration is checked in but intentionally not applied yet.

Preferred transition: enable a plan that supports private-repository protection, apply and verify this policy, then make the separate visibility decision. If that is not used, apply and verify protection immediately during the controlled public-visibility transition before announcing the repository.

Never change visibility merely to make an automation check pass.

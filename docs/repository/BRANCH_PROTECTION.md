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

## Verified public-repository configuration

On 2026-09-26 the owner explicitly approved public visibility after the clean-history cutover. The repository was made public and the canonical policy was applied and read back. Required reviews, CODEOWNERS, latest-push approval, all three strict check contexts, resolved conversations, linear history, and administrator enforcement are enabled; force pushes and branch deletion are disabled. Repository merges are squash-only, auto-merge is available after its gates pass, and merged branches are deleted automatically.

During preparation the private repository's plan rejected branch protection. On the public personal repository, GitHub also rejects even an empty `dismissal_restrictions` object: that field is reserved for organization repositories and must be omitted. The canonical script records this constraint. See the [GitHub branch-protection API](https://docs.github.com/en/rest/branches/branch-protection#update-branch-protection).

`CODEOWNERS` currently names `@MartinGroh` for every file. Contributor and Dependabot requests therefore require the owner's approval. GitHub does not let an author approve their own pull request. Owner-authored requests need another authorized code owner or a separately approved, documented emergency exception; the agent must not weaken the policy to merge its own request.

GitHub's generated pull-request test merge uses account email preferences independently of local Git configuration. Enable **Keep my email addresses private** for web-based Git operations and verify generated merge metadata before publication. During final verification, the publication request's generated merge failed the noreply-identity audit; the repository was temporarily returned to private. The owner approved enabling email privacy and recreating CAVE again. The setting is enabled, and a fresh generated merge passes the identity check. Reapply the required policy after publishing the replacement. See [GitHub's commit-email guidance](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/setting-your-commit-email-address).

Never change visibility merely to make an automation check pass.

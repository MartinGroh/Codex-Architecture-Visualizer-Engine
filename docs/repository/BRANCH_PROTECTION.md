# Branch Protection and Merge Policy

The protected branch is `main`.

## Required policy for the sole maintainer

- Changes enter through pull requests; direct pushes are blocked.
- `@MartinGroh` is the only account with write access. The owner's decision to merge after the required checks pass is approval; a separate approving review is not required.
- The pull-request requirement remains enabled with zero required approvals. CODEOWNERS and latest-push approval are not required because the sole maintainer cannot approve their own request.
- Required check contexts: `Build and test`, `Repository audit`, and `Gitleaks`, verified against GitHub's check-run API. The UI groups those jobs under CI, Public readiness, and Secret scan respectively; workflow-name prefixes are not part of their API context names.
- All review conversations must be resolved.
- Linear history is required; squash merge is the normal merge strategy.
- Force pushes and branch deletion are blocked.
- Administrators follow the same rules.

This policy applies to every pull request, including contributor and Dependabot requests. Outside contributors have no upstream write permission: they fork the public repository and open a pull request, and the owner decides whether to merge it. Passing checks alone does not grant merge permission. Auto-merge runs only when a writer explicitly enables it.

Before granting another person or GitHub App write/merge authority, review this policy. Restore `required_approving_review_count = 1`, `require_code_owner_reviews = true`, and `require_last_push_approval = true` in the canonical script, establish a second authorized CODEOWNER who can review owner-authored requests, and apply/read back the policy. `CODEOWNERS` continues to name the owner while separate review requirements are disabled.

Preview the exact API policy without changing GitHub:

```powershell
pwsh ./scripts/configure-branch-protection.ps1
```

Apply it only with repository-owner authority:

```powershell
pwsh ./scripts/configure-branch-protection.ps1 -Repository MartinGroh/Codex-Architecture-Visualizer-Engine -Branch main -Apply
```

## Verified public-repository configuration

On 2026-09-26 the owner explicitly approved public visibility after the clean-history cutover. The repository was made public and the original canonical policy was applied and read back with separate review, CODEOWNERS, and latest-push approval requirements. Those review requirements prevented the sole maintainer from merging owner-authored requests.

On 2026-09-27 the owner approved the sole-maintainer policy after confirming that only `@MartinGroh` has write access. The three separate approval requirements are disabled. Pull requests, all three strict check contexts, resolved conversations, linear history, and administrator enforcement remain required; force pushes and branch deletion remain disabled. Repository merges are squash-only, auto-merge is available after its gates pass, and merged branches are deleted automatically.

During preparation the private repository's plan rejected branch protection. On the public personal repository, GitHub also rejects even an empty `dismissal_restrictions` object: that field is reserved for organization repositories and must be omitted. The canonical script records this constraint. See the [GitHub branch-protection API](https://docs.github.com/en/rest/branches/branch-protection#update-branch-protection).

GitHub does not let an author approve their own pull request. The owner explicitly authorized this policy correction; an agent must not change merge policy merely to unblock its own request. Retain the `required_pull_request_reviews` object when setting the approval count to zero: removing that object would remove the pull-request requirement. See [GitHub's protection settings](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule).

GitHub's generated pull-request test merge uses account email preferences independently of local Git configuration. Enable **Keep my email addresses private** for web-based Git operations and verify generated merge metadata before publication. During final verification, the publication request's generated merge failed the noreply-identity audit; the repository was temporarily returned to private. The owner approved enabling email privacy and recreating CAVE again. The setting is enabled, and a fresh generated merge passes the identity check. Reapply the required policy after publishing the replacement. See [GitHub's commit-email guidance](https://docs.github.com/en/account-and-profile/how-tos/email-preferences/setting-your-commit-email-address).

Never change visibility merely to make an automation check pass.

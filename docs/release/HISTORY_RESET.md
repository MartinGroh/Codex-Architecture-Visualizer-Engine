# Clean-history cutover

Approved by the repository owner on 2026-09-26: check in verified preparation, remove old Git history, and delete/recreate the private GitHub repository at the same URL. Public visibility requires a later explicit decision.

## Why repository recreation is included

The old repository has 15 pull requests whose read-only Git refs retain old ancestry. Rewriting ordinary branches would leave those refs and old Actions artifacts available. GitHub documents these limitations in [Removing sensitive data from a repository](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).

Before cutover, the repository has no forks, releases, stars, external collaborators, or webhooks. Wiki and Discussions are disabled. Its Dependabot pull requests and old Actions runs are disposable under the owner's recreation approval.

## Procedure

1. Verify the source and fresh synthetic demo, then check in preparation using the owner's GitHub noreply identity.
2. Create a parentless commit with the exact verified source tree. Verify its tree, parent count, and author/committer identities.
3. Delete and recreate the repository **privately**, at the same owner/name. Verify its new repository identity before pushing only the clean main branch.
4. Check advertised Git refs, old commit and pull-request URLs, Actions runs/artifacts, repository settings, and the final workflows.
5. Preserve existing local review files and index state. Replace their old base history with a sanitized parentless baseline containing the same base tree. Remove obsolete refs, expire old reflogs, and prune old Git objects; verify the review diff remains identical.
6. Clone the recreated remote into a fresh directory, compare its source tree, and run complete-history and current-source audits.
7. Update evidence and verify the final pushed commit's checks. Present the completed preparation for the owner's public-visibility decision.
8. After explicit public approval, apply/read back canonical branch protection, enable available security reporting/protections, run public security checks, and verify unauthenticated access.

## Existing clones and limits

Use a fresh clone of the recreated repository. Reapply any unpublished file changes without merging or pushing old ancestry. Do not retain an old branch as a public backup.

This procedure removes old history from the replacement repository's exposed Git references and from the reviewed local Git database. It does not erase independently held clones, platform backups, or ignored local runtime data. Local journals and private review captures remain excluded from published source. No claim of physical erasure from GitHub's internal backups is made.

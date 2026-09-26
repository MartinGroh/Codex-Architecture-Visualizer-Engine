# Clean-history cutover

Approved by the repository owner on 2026-09-26: check in verified preparation, remove old Git history, and delete/recreate the private GitHub repository at the same URL. Public visibility requires a later explicit decision.

The owner subsequently approved public visibility on the same date. Publication, branch protection, public security scans, and signed-out access checks are recorded in [PUBLIC_READINESS.md](PUBLIC_READINESS.md) and the [public-release checklist](../security/PUBLIC_RELEASE_CHECKLIST.md).

## Why repository recreation is included

The old repository has 15 pull requests whose read-only Git refs retain old ancestry. Rewriting ordinary branches would leave those refs and old Actions artifacts available. GitHub documents these limitations in [Removing sensitive data from a repository](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository).

Before cutover, the repository had no forks, releases, stars, external collaborators, or webhooks. Wiki and Discussions were disabled. Its Dependabot pull requests and old Actions runs were disposable under the owner's recreation approval.

## Procedure

1. Verify the source and fresh synthetic demo, then check in preparation using the owner's GitHub noreply identity.
2. Create a parentless commit with the exact verified source tree. Verify its tree, parent count, and author/committer identities.
3. Delete and recreate the repository **privately**, at the same owner/name. Verify its new repository identity before pushing only the clean main branch.
4. Check advertised Git refs, old commit availability and pull-request ancestry, Actions runs/artifacts, repository settings, and the final workflows. Pull-request numbers can be reused by new requests after recreation.
5. Preserve existing local review files and index state. Replace their old base history with a sanitized parentless baseline containing the same base tree. Remove obsolete refs, expire old reflogs, and prune old Git objects; verify the review diff remains identical.
6. Clone the recreated remote into a fresh directory, compare its source tree, and run complete-history and current-source audits.
7. Update evidence and verify the final pushed commit's checks. Present the completed preparation for the owner's public-visibility decision.
8. After explicit public approval, apply/read back canonical branch protection, enable available security reporting/protections, run public security checks, and verify unauthenticated access.

## Existing clones and limits

Use a fresh clone of the recreated repository. Reapply any unpublished file changes without merging or pushing old ancestry. Do not retain an old branch as a public backup.

This procedure removes old history from the replacement repository's exposed Git references and from the reviewed local Git database. It does not erase independently held clones, platform backups, or ignored local runtime data. Local journals and private review captures remain excluded from published source. No claim of physical erasure from GitHub's internal backups is made.

## Completed cutover evidence

On 2026-09-26 the repository was recreated privately at the same URL with new GitHub repository identity `1388982522`. The initial clean commit contains the exact checked-in preparation tree and has no parent. Subsequent audit fixes remain descendants of that clean root.

All 35 recorded old commit and remote-ref object identities were unavailable through the replacement repository's commit API. Fetching the old main commit was rejected. A fresh clone fetched every advertised branch and pull-request ref: all 24 commits at that checkpoint descended from the one clean root and passed the complete-history audit. New Dependabot requests were created after recreation; their numbers may overlap the deleted requests.

The replacement repository's Actions runs and artifacts were created after recreation. Existing local review status, index bytes, complete diff, and all changed/untracked file hashes were identical after replacing their baseline with the same tree and sanitized identity. Old reflogs were expired and recorded old commit objects were pruned from the reviewed Git database. No user review files were removed.

## Second recreation for GitHub-generated commit metadata

Later verification found a non-noreply address in the first replacement's generated pull-request merge metadata. Publication was immediately paused. The owner approved enabling GitHub email privacy and recreating CAVE again, with all repository changes confined to CAVE.

On 2026-09-26 the privacy setting was enabled and verified after reload. A fresh generated merge used safe noreply identities, and corrected source revision `2286a18924e7d90960e126739c37e60a7e0a3e57` passed CI, Gitleaks, and the complete-history audit. Repository identity `1388982522` was deleted with its 12 pull requests and Actions records; GitHub confirmed deletion and the repository API returned 404.

The second replacement was created **privately** with identity `1389072240`, preserving the same URL. It retains only the reviewed clean ancestry rooted at `2466227d04afdb5f39ad8e98fc24075d954c96dd`. Existing local review work is preserved. Recheck all advertised refs, generated merge identities, final workflows, and public controls before completing publication. This removes exposed repository records; it does not certify erasure of independently retained copies or platform backups.

A fresh clone fetched every advertised branch and pull-request ref. All 16 commits at this private checkpoint pass the source and complete-history audits. GitHub's commit API rejects the failed generated merge from the deleted replacement. The final public check repeats this audit because new pull requests can add refs after the checkpoint.

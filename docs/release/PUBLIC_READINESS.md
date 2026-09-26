# Public-readiness assessment

Reviewed 2026-09-26. Source preparation and the clean-history cutover are verified. Keep the repository private until final pushed-head checks pass and the owner makes an explicit public-visibility decision. See [PUBLIC_RELEASE_CHECKLIST.md](../security/PUBLIC_RELEASE_CHECKLIST.md).

## Source and engineering evidence

The review covered the current source, reachable historical text, commit identities, dependency metadata, governance documents, and tracked media. No credential, private key, private endpoint, customer data, or retained conversation was identified in the reviewed source. Earlier history contains developer-local fixture paths and two non-noreply email identities; repository recreation and local history cleanup are approved to remove that ancestry.

The canonical source audit passes. It rejects generated private state, sensitive filenames, candidate secrets, private keys, developer paths, and organization-specific terms. Its complete-history mode now also rejects non-GitHub-noreply author and committer addresses. Findings suppress values. The public-readiness workflow fetches complete history and audits it on normal pushes and pull requests.

These are detection results, not a guarantee that every possible secret format is absent. Image pixels, video frames, archive contents, and runtime network security require separate review.

Canonical `scripts/verify.ps1` passes: 149 backend tests, 174 frontend tests, 34 hook tests, launcher/observation checks, build/lint, dependency audits, plugin validation, CodeGraph synchronization/status, and launched live health/snapshot checks. A synthetic browser check confirmed attributed text, tables, writing blocks, comments, and suggestions. No plugin installation or live Codex turn mutation was performed.

A fresh archive of the reviewed source contains 298 files and no Git history or copied CodeGraph state. The canonical demo launcher builds successfully and returns healthy, explicitly synthetic workspace, activity, chat, and usage data, with Codex send disabled. Verification used PowerShell 7.6.6, .NET SDK 10.0.303, and Node 26.8.2; GitHub CI separately exercises its configured Node version. The temporary demo processes were stopped after verification. A fresh remote clone also passes source/history audits and matches the current reviewed source tree.

## Media and local state

All ten tracked demo PNGs show the synthetic Atlas demo and contain no PNG metadata fields. Both tracked MP4s were sampled at one frame per second throughout their durations. Samples show the synthetic demo; both videos have no audio and only container/encoder metadata. This is sampled review, not inspection of every frame.

Brand provenance is documented in [plugins/cave/assets/README.md](../../plugins/cave/assets/README.md). Ignored local review captures, journals, catalogs, `.cave/`, `.codegraph/`, generated binaries, and logs are private runtime state. They remain excluded from source publication and must not be force-added or attached to public issues.

## History and GitHub controls

The owner approved check-in followed by old-history removal and deletion/recreation of the private repository at the same URL. This is complete. The replacement is private, has a new GitHub identity, and begins with a parentless commit containing the exact verified preparation tree. The 15 old pull-request records and old Actions artifacts were removed with the old repository.

All 35 recorded old commit/ref identities were unavailable through the new repository's commit API, and fetching the old main commit was rejected. The fresh clone fetched every advertised branch and pull-request ref; all 24 commits at the checkpoint descended from one clean root and passed the history audit. New Dependabot requests can reuse the old request numbers, but their ancestry contains only the new clean root. Runs and artifacts in the replacement repository were created after recreation. Existing local review files/index/diff were preserved exactly; old reflogs and recorded old commit objects were removed from the reviewed local Git database. The procedure, evidence, and limits are recorded in [HISTORY_RESET.md](HISTORY_RESET.md).

Required branch-check names now match actual GitHub checks: `Build and test`, `Repository audit`, and `Gitleaks`. The private plan returns HTTP 403 for branch protection; the policy must be applied and read back immediately at the approved public transition. Wiki and Discussions stay disabled. Security reporting points to the advisory reporting area, with the maintainer's available private profile contact as a fallback until public vulnerability reporting can be enabled.

The history audit's successful exit handling was verified both locally and in GitHub after correcting propagation of Git's expected no-match result. A subsequent CI run exposed a test race: the durable delivery record can become Completed before the separately written control status becomes Ready. The test now waits for both and always stops/joins the worker before fixture deletion, including on assertion failure. All 11 bridge tests and three repeated completion-test runs pass locally. The cutover source revision `fa3390b09a8d9223548e7ff40c9bd81b4d20fc14` passes GitHub CI, Gitleaks, and the complete-history public-readiness workflow. Recheck the final pushed head before transition.

Repository description, homepage, topics, squash-only merge settings, and owner-only access are verified. Making it public, enabling the public security controls, and verifying access without authentication are final transition gates.

## Runtime boundary and functional limitation

The host defaults to an unauthenticated development listener. Browser access includes conversation-sharing changes, task chat, isolated node questions, semantic edits, and undo/redo. Reachable callers must be authorized for both shared content and these operations. Task/revision checks are routing and concurrency controls, not authentication. Source publication does not authorize internet exposure of this service.

Privacy documentation now accurately describes local chat journals, optional sharing, machine/account information, configured Codex providers, and deletion behavior. Disabling sharing does not erase journals.

A functional follow-up remains: edit batches do not include the client's expected workspace revision, so a stale browser draft can overwrite a newer edit. Adding that check changes the shared browser/MCP contract and awaits the owner's architecture approval. Save-time cross-process comparison alone does not reject a draft based on an older client read.

## Dependencies and separate binary release gates

The reviewed npm and six-project .NET dependency audits report no known vulnerabilities. CAVE source is MIT licensed. Dependency license metadata includes permissive licenses, MPL-2.0, and ELK's `EPL-2.0 OR GPL-3.0-or-later`; CAVE's license does not replace them.

The current packaging script does not yet stage a complete CAVE/third-party notice set or require a clean reviewed tree before recording the package revision. ELK is included in the shipped UI, so its chosen distribution license and covered-source information must be addressed before distributing binaries. Review the actual shipped dependencies; development-only license entries do not alone establish binary obligations.

No binary release or directory submission is published in this preparation. Archive inspection, complete notices, installation, live workspace, update, and rollback verification remain separate release gates. This source assessment does not certify those artifacts, every video frame, platform backups, other clones, or deployment security.

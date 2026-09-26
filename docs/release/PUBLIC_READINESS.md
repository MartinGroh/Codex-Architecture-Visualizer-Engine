# Public-readiness assessment

Reviewed 2026-09-26. Source preparation and the clean-history cutover are verified. The owner approved public visibility, but a subsequent generated pull-request merge failed the email-identity audit. The repository was temporarily returned to private pending remediation. See [PUBLIC_RELEASE_CHECKLIST.md](../security/PUBLIC_RELEASE_CHECKLIST.md).

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

The owner approved check-in followed by old-history removal and deletion/recreation of the private repository at the same URL. This is complete. The replacement was prepared privately, has a new GitHub identity, and begins with a parentless commit containing the exact verified preparation tree. The 15 old pull-request records and old Actions artifacts were removed with the old repository. The owner subsequently approved making the verified replacement public.

All 35 recorded old commit/ref identities were unavailable through the new repository's commit API, and fetching the old main commit was rejected. The fresh clone fetched every advertised branch and pull-request ref; all 24 commits at the checkpoint descended from one clean root and passed the history audit. New Dependabot requests can reuse the old request numbers, but their ancestry contains only the new clean root. Runs and artifacts in the replacement repository were created after recreation. Existing local review files/index/diff were preserved exactly; old reflogs and recorded old commit objects were removed from the reviewed local Git database. The procedure, evidence, and limits are recorded in [HISTORY_RESET.md](HISTORY_RESET.md).

Required branch-check names match actual GitHub checks: `Build and test`, `Repository audit`, and `Gitleaks`. The public transition applied and read back strict required checks, one approving review, CODEOWNERS review, stale-review dismissal, latest-push approval, resolved conversations, linear history, and administrator enforcement. Direct main pushes, force pushes, and branch deletion are blocked. Merge settings allow squash only. Wiki and Discussions stay disabled. Private vulnerability reporting is enabled and its reporting link is visible while signed out. Secret scanning, push protection, Dependabot alerts, and automated security updates are enabled; the first public read-back found no open secret-scanning or Dependabot alerts.

The history audit's successful exit handling was verified both locally and in GitHub after correcting propagation of Git's expected no-match result. A subsequent CI run exposed a test race: the durable delivery record can become Completed before the separately written control status becomes Ready. The test now waits for both and always stops/joins the worker before fixture deletion, including on assertion failure. All 11 bridge tests and three repeated completion-test runs pass locally. The final publication source revision `6422c84e827c4583bb74842b094c8403c45bf109` passes [GitHub CI](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/actions/runs/36234335309), [Gitleaks](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/actions/runs/36234335314), and the [complete-history public-readiness workflow](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/actions/runs/36234335321).

Repository description, homepage, topics, merge settings, and owner-only collaborator access are verified. A signed-out browser shows the public source and confidential reporting link. Unauthenticated requests resolve the license, contribution, support, and security pages and all four workflow badges. A fresh clone with credential helpers and authorization headers disabled fetched every advertised branch and pull-request ref: all 26 commits at the public checkpoint descend from the clean root and pass the current-tree/history audit. Its main revision is the verified publication source. An unauthenticated request for the recorded old preparation commit returns 404.

The [public CodeQL run](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/actions/runs/36235359832) succeeds for both C# and JavaScript/TypeScript on that same publication revision. GitHub's analysis records report zero results, no errors, and no warnings for each language; there are no open code-scanning alerts at the checkpoint. These scans supplement the source/history and media review.

Final verification of the publication documentation pull request caught a new non-noreply author address in GitHub's automatically generated test merge. The source branch and main commits still use the reviewed noreply identity. The repository was immediately returned to private; account-wide GitHub email privacy and removal of the generated PR metadata require additional owner approval. The main revision's green checks and the earlier 26-commit public checkpoint do not certify this later merge ref. Recheck all advertised refs, final workflows, protection, and public access after remediation. The repository was briefly public; restricting visibility does not erase independently retained copies.

The pull-request CI also exposed a test-fixture dependency on the checkout's tracked upstream. The GitHub checkout is detached, so that baseline is unavailable. The sample snapshot test now explicitly requests HEAD and asserts that baseline, preserving the production upstream policy. The unmodified test failure was reproduced in a detached verification clone; after the correction all 13 host tests and all 149 backend tests pass there. The corrected test is prepared locally for the approved remediation.

## Runtime boundary and functional limitation

The host defaults to an unauthenticated development listener. Browser access includes conversation-sharing changes, task chat, isolated node questions, semantic edits, and undo/redo. Reachable callers must be authorized for both shared content and these operations. Task/revision checks are routing and concurrency controls, not authentication. Source publication does not authorize internet exposure of this service.

Privacy documentation now accurately describes local chat journals, optional sharing, machine/account information, configured Codex providers, and deletion behavior. Disabling sharing does not erase journals.

A functional follow-up remains: edit batches do not include the client's expected workspace revision, so a stale browser draft can overwrite a newer edit. Adding that check changes the shared browser/MCP contract and awaits the owner's architecture approval. Save-time cross-process comparison alone does not reject a draft based on an older client read.

## Dependencies and separate binary release gates

The reviewed npm and six-project .NET dependency audits report no known vulnerabilities. CAVE source is MIT licensed. Dependency license metadata includes permissive licenses, MPL-2.0, and ELK's `EPL-2.0 OR GPL-3.0-or-later`; CAVE's license does not replace them.

The current packaging script does not yet stage a complete CAVE/third-party notice set or require a clean reviewed tree before recording the package revision. ELK is included in the shipped UI, so its chosen distribution license and covered-source information must be addressed before distributing binaries. Review the actual shipped dependencies; development-only license entries do not alone establish binary obligations.

No binary release or directory submission is published in this preparation. Archive inspection, complete notices, installation, live workspace, update, and rollback verification remain separate release gates. This source assessment does not certify those artifacts, every video frame, platform backups, other clones, or deployment security.

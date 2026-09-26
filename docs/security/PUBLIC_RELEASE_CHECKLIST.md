# Public Source Publication Checklist

Keep the repository private until the clean-history cutover is verified and the owner explicitly approves public visibility. Binary releases have separate gates below.

## Verified source preparation

- [x] Current source audit passes; generated private state is excluded.
- [x] Source, historical text, commit identities, and tracked media were reviewed; no credential leak was identified.
- [x] Ten synthetic demo images and sampled frames throughout both demo videos were reviewed. Videos have no audio.
- [x] Canonical verification passes: 149 backend, 174 frontend, and 34 hook tests, build/lint, dependency checks, plugin validation, and live health/snapshot checks.
- [x] A fresh source archive builds and runs the synthetic demo without Git history or an external CodeGraph workspace.
- [x] Privacy and network-boundary documentation accurately describes browser reads and writes, local journals, and configured Codex providers.
- [x] Security reporting, contribution rules, governance, code of conduct, source license, issue templates, and architecture documents were reviewed.
- [x] Required branch-check names match GitHub's actual check names.
- [x] Wiki and Discussions remain disabled; reviewed wiki source lives in `docs/wiki`.
- [x] The owner approved history removal and private repository recreation after verification.

## Clean-history cutover

- [ ] Preparation changes are checked in using a GitHub noreply identity.
- [ ] GitHub repository is recreated privately at the same URL with a new repository identity.
- [ ] Published source starts at a parentless commit containing the exact verified tree.
- [ ] Old branch, pull-request, commit, Actions, and artifact references are unavailable in the recreated repository.
- [ ] Existing local review work is preserved; local Git refs and objects containing old history are removed.
- [ ] A fresh remote clone passes the current-tree and complete-history audits, including commit identities.
- [ ] CI, Gitleaks, and the history-enabled public-readiness workflow pass on the final pushed commit.
- [ ] Repository metadata, merge settings, and collaborator access are verified.

See [HISTORY_RESET.md](../release/HISTORY_RESET.md) for the approved procedure and limits, and [PUBLIC_READINESS.md](../release/PUBLIC_READINESS.md) for evidence.

## Controlled public transition

- [ ] The owner records an explicit **make public** decision after reviewing the verified cutover.
- [ ] Visibility changes in a controlled window.
- [ ] Canonical branch protection is applied and read back; the current private plan does not support it.
- [ ] Private vulnerability reporting, available secret-scanning protections, Dependabot, and CodeQL are enabled and verified.
- [ ] Public source, links, badges, security reporting, and clone instructions are checked without authentication.

Follow [BRANCH_PROTECTION.md](../repository/BRANCH_PROTECTION.md). Source publication does not authorize exposing an unauthenticated running host to an untrusted network.

## Separate binary release and directory gates

These gates remain deferred until a binary release or directory submission is requested. Source publication does not certify them.

- [ ] Build a release archive from a clean reviewed commit and record its SHA-256.
- [ ] Inspect archive contents; exclude private runtime state, journals, logs, and local captures.
- [ ] Include CAVE's license and required third-party notices, including the chosen ELK license and applicable covered-source information.
- [ ] Verify installation, health, demo, live workspace, update, and rollback from the actual release artifact.
- [ ] Obtain explicit approval for release publication or Codex Plugins Directory submission and complete that channel's checklist.

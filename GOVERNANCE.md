# Governance

CAVE uses a maintainer-led, evidence-based model while the contributor community grows.

## Roles

**Contributors** report issues, propose designs, improve documentation, and submit pull requests.

**Reviewers** are trusted contributors who regularly provide accurate, constructive review in an area. Reviewers can approve changes but cannot merge unless they are also maintainers.

**Maintainers** own repository administration, security response, releases, marketplace submissions, architecture decisions, and final merge authority. The initial maintainer is the repository owner.

New reviewers and maintainers are added by existing maintainers based on sustained contributions, sound judgment, respectful conduct, and demonstrated care for privacy and compatibility. Role changes are announced in a repository discussion or governance pull request.

## Decisions

- Routine changes use normal pull-request review.
- Cross-boundary architecture changes start with a design issue and, when accepted, an architecture decision record.
- Security response may be handled privately until a coordinated disclosure is safe.
- Maintainers seek consensus. If consensus cannot be reached, the repository owner makes the decision and records the reasoning.

Major decisions must identify the owner, alternatives, compatibility impact, privacy impact, verification, delivery, and rollback.

## Releases and publication

A maintainer may create a GitHub release after CI, packaging, public-readiness review, and changelog approval. A release tag or GitHub release never publishes the plugin to the Codex Plugins Directory. Marketplace submission and the final publish click require a separate maintainer decision under [docs/release/CODEX_PLUGIN_SUBMISSION.md](docs/release/CODEX_PLUGIN_SUBMISSION.md).

Making the repository public is a separate owner-controlled action gated by [docs/security/PUBLIC_RELEASE_CHECKLIST.md](docs/security/PUBLIC_RELEASE_CHECKLIST.md).

## Repository administration

Protected-branch requirements, CODEOWNERS, required checks, merge policy, and release permissions are documented in [docs/repository/BRANCH_PROTECTION.md](docs/repository/BRANCH_PROTECTION.md). Administrative exceptions should be rare, time-bounded, and explained in the affected pull request.

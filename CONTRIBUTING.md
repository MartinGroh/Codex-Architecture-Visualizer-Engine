# Contributing to CAVE

Thank you for helping make software architecture easier to see. CAVE welcomes bug reports, documentation fixes, tests, design discussion, and focused code changes.

## Before you start

Read:

1. [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
2. [ArchitectureAndCodeGuidlines.md](ArchitectureAndCodeGuidlines.md).
3. [Architecture.md](Architecture.md) and [AGENTS.md](AGENTS.md) for CAVE-specific boundaries.
4. The closest design document or accepted decision for the area you will change.

Use an issue before substantial work. A design issue is required for new service boundaries, persistence, protocols, provider replacement, public contracts, or changes to privacy defaults. Small bug fixes and documentation corrections can go straight to a pull request.

## What contributions may do

- Fix a reproducible defect while preserving accepted behavior.
- Add tests, diagnostics, accessibility, documentation, or performance improvements.
- Add a provider behind an application-owned port and explicit composition mode.
- Improve demo content when every value remains synthetic and clearly labeled.
- Propose functionality from [WANTED.md](WANTED.md).

## What contributions may not do

- Introduce silent fallbacks, shadow implementations, patch wrappers, or a second owner for existing behavior.
- Mix sample, inferred, declared, and observed evidence without provenance.
- Enable conversation sharing by default or collect prompts, reasoning, commands, tool payloads, or final replies through activity hooks.
- Add credentials, personal paths, private endpoints, internal identifiers, customer data, generated indexes, journals, logs, or real-workspace captures.
- Expose the unauthenticated browser host to an untrusted network.
- Change infrastructure, network policy, release credentials, or persistent user data as part of a code contribution.
- Replace repository-owned build, package, install, update, rollback, or verification paths without an accepted design decision.

## Local setup

Start with the synthetic demo:

```powershell
pwsh ./scripts/start-demo.ps1
```

For development and focused verification:

```powershell
npm --prefix src/Cave.Ui ci
npm --prefix src/Cave.Ui run lint
npm --prefix src/Cave.Ui run test
npm --prefix src/Cave.Ui run build
dotnet build CAVE.slnx
dotnet test CAVE.slnx --no-build
pwsh ./scripts/audit-public-readiness.ps1
```

The full live-provider verification additionally requires CodeGraph and the local Codex plugin validator:

```powershell
pwsh ./scripts/verify.ps1
```

## Pull-request workflow

1. Create a focused branch from current `main`.
2. Keep unrelated local changes out of the pull request.
3. Add or update tests at the boundary that changed.
4. Update docs, decisions, release notes, and privacy notes when applicable.
5. Run the relevant checks and include exact commands and outcomes in the pull request.
6. Complete the pull-request template and link the issue.
7. Respond to review without force-pushing away reviewable history unless a maintainer requests it.

Pull requests are squash-merged after required checks, review, and conversation resolution. Maintainers may close stale, unsafe, or architecturally incompatible changes with an explanation.

## Coding notes

- Backend: stable .NET 10, C# 14, nullable enabled, warnings as errors, public XML documentation.
- Frontend: React, strict TypeScript, Vitest, and the checked-in lint configuration.
- Tests should state the behavioral contract, not reproduce implementation detail.
- Comments should explain constraints and reasons, not restate code.
- Avoid drive-by formatting and dependency updates in a functional change.

## Licensing

By contributing, you confirm that you have the right to submit the work and agree that it is licensed under the repository's [MIT License](LICENSE).

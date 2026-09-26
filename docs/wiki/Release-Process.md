# Release Process

CAVE uses Semantic Versioning and a checked-in `VERSION` file.

```powershell
pwsh ./scripts/set-version.ps1 -Version 0.4.0 -Apply
pwsh ./scripts/verify.ps1
pwsh ./scripts/audit-public-readiness.ps1
pwsh ./scripts/package-release.ps1
```

A maintainer reviews the changelog and generated archive, creates an annotated `v<version>` tag, and lets the release workflow create a draft GitHub release. GitHub release creation never publishes the plugin to the Codex Plugins Directory.

Directory publication has an additional manual gate: public production MCP hosting where applicable, verified identity and domain, listing metadata, privacy and terms pages, test prompts, skill scan, review, and an explicit maintainer publish decision.

The canonical details are in [docs/release/RELEASE_PROCESS.md](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/release/RELEASE_PROCESS.md) and [docs/release/CODEX_PLUGIN_SUBMISSION.md](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/release/CODEX_PLUGIN_SUBMISSION.md).

# CAVE agent guide

## Authority

- Read the active Codex home's `Architecture.md` completely before development work.
- Read this repository's `Architecture.md` and `docs/design/Codex_Architecture_Visualizer_Design.md` completely before changing architecture, plugin packaging, the backend, or the frontend.
- The design document is product input. Explicit user requests and this repository's accepted ADRs govern implementation choices when it is outdated or ambiguous.
- Preserve the global dependency-direction, canonical-path, no-hidden-fallback, and verification rules.

## Code intelligence

- This repository is initialized for CodeGraph. Use the CodeGraph MCP tools first for semantic code discovery when they are available in the current task.
- If the MCP tool is unavailable in the current task, use `codegraph explore`, `codegraph node`, or `codegraph query` from the repository root and state that the shell path was used.
- Do not query `.codegraph/codegraph.db` directly. CodeGraph is an external semantic provider behind an application-owned port.
- `.codegraph/` is local index state and must stay uncommitted.

## Architecture and ownership

- `Cave.Domain` owns stable architecture graph concepts and invariants. It has no infrastructure or host dependencies.
- `Cave.Application` owns use cases and consumer-owned ports such as semantic indexing. It depends only on `Cave.Domain`.
- `Cave.Infrastructure` owns CodeGraph, Git, filesystem, and process adapters. It implements application ports and depends inward.
- `Cave.Host` is the ASP.NET Core composition root and HTTP/MCP presentation host. Route handlers remain thin.
- `src/Cave.Ui` is the single React/TypeScript frontend for embedded and browser views.
- `plugins/cave` is the distributable Codex plugin. Do not create a second personal source copy.
- Static semantic truth, Git truth, observed activity, and agent-declared intent remain separate inputs and overlays.

## Stack and quality

- Target stable .NET 10 and C# 14. Do not adopt .NET 11 preview APIs unless the user explicitly requests them.
- Use ASP.NET Core Minimal APIs for the current focused host surface, built-in DI/configuration/logging/ProblemDetails/health checks, and SSE for one-way live updates.
- Use React and strict TypeScript. Use React Flow for graph interaction and keep layout behind one frontend-owned interface.
- Document every public C# type and member with accurate XML documentation.
- A sample/demo semantic source is permitted only as an explicit acceptance-spike mode. It must be labeled in API/UI output and must never activate as a silent fallback after CodeGraph fails.

## Canonical commands

```powershell
dotnet build CAVE.slnx
dotnet test CAVE.slnx
npm --prefix src/Cave.Ui ci
npm --prefix src/Cave.Ui run build
npm --prefix src/Cave.Ui run test
pwsh ./scripts/install-local-plugin.ps1
```

- Use repository scripts for plugin packaging and installation once present; do not hand-edit marketplace state.
- Verify backend tests, frontend tests/build, plugin validation, CodeGraph status, and a launched `/health` plus `/api/snapshot` check before delivery.


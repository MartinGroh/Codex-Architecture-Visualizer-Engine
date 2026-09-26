---
name: open
description: Open an already-initialized repository's live CAVE architecture graph inside Codex through the bundled MCP App. Use when the user asks to open or reopen CAVE, invokes `/cave:open` or `$cave:open`, wants the live architecture view in Codex, or wants to avoid the browser viewer.
---

# Open CAVE

Open exactly one initialized repository through CAVE's canonical MCP App.

## Workflow

1. Resolve the requested repository root. Use an explicitly supplied path; otherwise resolve the active Codex project's Git root. Do not guess between multiple repositories.
2. Require a `.codegraph` directory at that exact root. If it is absent, stop and tell the user to run `/cave:init`; do not initialize through a second path.
3. Call `cave_show_architecture` immediately with the absolute repository root. This tool owns workspace registration, index synchronization, viewer supervision, the initial snapshot, and the live subscription.
4. Keep the returned MCP App open inside Codex. The host chooses inline, fullscreen, or picture-in-picture placement. Do not open an external browser as a silent fallback.
5. Report the opened workspace and live graph version. Describe placement accurately if the host declines the requested larger presentation.

## Failure handling

- Stop on an unresolved Git root, missing `.codegraph` state, unavailable `cave_show_architecture` tool, or tool failure.
- Preserve the reported path and cause. Do not choose a parent repository, start an alternate viewer, or claim the graph is live when the tool did not complete.
- If the skill is visible but its MCP tool is unavailable, tell the user to reinstall the CAVE plugin and start a new Codex task so the updated tool set can load.

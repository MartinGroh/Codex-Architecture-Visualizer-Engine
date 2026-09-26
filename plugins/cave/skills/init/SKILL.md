---
name: init
description: Initialize the current Git workspace for CAVE by validating the pinned CodeGraph runtime, protecting generated local state, building the first semantic index when needed, registering the workspace, and opening its live architecture graph. Use when the user asks for CAVE init, `/cave:init`, `$cave:init`, CAVE setup in a repository, or adding CAVE to another project.
---

# Initialize CAVE

Initialize exactly one repository and leave existing source and configuration unchanged except for missing CAVE ignore entries.

## Workflow

1. Resolve the requested workspace from the active Codex project. If the user supplied a path, use that path. Do not guess between multiple repositories.
2. Run `scripts/initialize-cave-workspace.ps1` from this skill directory with the absolute candidate path:

   ```powershell
   pwsh -NoProfile -File <skill-directory>/scripts/initialize-cave-workspace.ps1 -WorkspacePath <absolute-path>
   ```

   The script resolves the containing Git root, requires CodeGraph 1.5.0, adds only missing `.codegraph/` and `.cave/` ignore entries, and creates the initial index only when one is absent. Do not reproduce or bypass these checks, and never use CodeGraph's `--force` option.
3. Parse the script's JSON result and call `cave_show_architecture` with its exact `workspaceRoot`. This existing tool registers the workspace in CAVE's machine catalog, synchronizes the index, and opens the live graph.
4. Report the initialized root, whether the index was created or reused, any ignore entries added, and that the graph is open. Also report `activityHookObserved` and `activityHookStatus`. Only `CurrentSession` proves that this task emitted a real CAVE hook event. `HistoricalOnly` means older tasks emitted events but this task is not connected. If `activityHookObserved` is false, explain that architecture remains available but live agent activity for this task is not connected; tell the user to open `/hooks`, trust and enable the installed CAVE hook definition, and then start a new task.

## Failure handling

- Stop on a missing Git root, missing CodeGraph installation, non-1.5.0 CodeGraph version, indexing failure, or unavailable `cave_show_architecture` tool.
- Preserve the reported path and cause. Do not initialize a parent directory, install another CodeGraph version, or claim registration succeeded when the graph tool did not complete.
- A false `activityHookObserved` value is a visible degraded state, not an initialization failure. Never claim live agent activity is connected from historical journal files; require `activityHookStatus: CurrentSession`.

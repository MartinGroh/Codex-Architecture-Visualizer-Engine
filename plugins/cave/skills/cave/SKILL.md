---
name: cave
description: Inspect and explain a workspace architecture, dependency impact, Git change overlays, and active Codex work through CAVE and CodeGraph. Use when the user asks to map, visualize, monitor, or understand the architecture of a codebase or the impact of a change.
---

# CAVE

Use deterministic code and change evidence first. Keep static code truth, Git truth, observed activity, and agent-declared intent distinct in every answer.

## Workflow

1. Confirm the workspace root and whether `.codegraph/` exists.
2. Call `cave_show_architecture` with the absolute repository root as soon as the user asks to see or monitor architecture. Keep its MCP App open while work continues; it refreshes from source, activity, and declared-scope changes.
3. As soon as an agent or subagent begins bounded work, call `cave_set_scope` with an exact active scope and concise summary. Update the declaration when scope changes and mark it completed when the work finishes. Never declare guessed project, namespace, class, or file names.
4. Use `cave_poll_architecture` only from the bundled app. For model-side semantic questions, use CodeGraph MCP tools; if they are unavailable, use `codegraph explore`, `codegraph node`, or `codegraph query` from the workspace root and clearly identify that shell path.
5. Use Git for change status and line deltas. Do not infer rationale from a diff.
6. Treat recorded scope and intent as agent-declared statements, not observed structural facts.
7. Call static edges dependencies. Do not call them runtime data flow.

## Evidence boundary

The live graph observes Codex lifecycle/tool hooks and workspace file changes. Hook activity is objective session evidence; `cave_set_scope` is agent-declared evidence. Neither proves runtime execution flow or rationale. Never claim those facts unless a separate authoritative CAVE source returned them.

## Successful result

Return the smallest useful architecture view, identify evidence and confidence, distinguish dependency direction from impact direction, and state any unavailable data rather than synthesizing it.

# Codex compatibility

Reviewed against the installed Codex CLI **0.157.0** on 2026-09-26 and its generated experimental App Server JSON schemas. This records CAVE's integration surface; it is not a claim to reproduce every Codex Desktop feature.

## Supported public-text presentation

`CodexText` is the frontend owner shared by the conversation panel and isolated node-answer dialog. It reads retained public text without modifying journal records or executing embedded instructions.

| Format | CAVE presentation |
| --- | --- |
| CommonMark and GitHub-flavored Markdown | headings, links, code, tables, task lists, strikethrough, footnotes |
| `:::writing{...}` | draft card with variant, subject, supplied recipient fields, and Markdown body |
| `::code-comment{...}` | read-only comment card with title, body, file and line attribution |
| `::created-thread{...}` | read-only created-chat identity |
| `:codex-followup[...] {...}` without the separating space | suggestion label and prompt tooltip; no automatic submission |
| Codex `cite` / `filecite` markers | readable source identifiers; no invented links |

Writing variants are `email`, `chat_message`, `social_post`, `document`, and `standard`. Quoted attributes support JSON string escapes. Numeric attributes remain display values. Malformed, unknown and future directive forms stay visible through the ordinary Markdown path. Fenced and inline code preserve literal examples. An incomplete writing block retains its body while a public message streams. User prompts receive Markdown rendering but do not activate Codex attribution decoding.

The hook contract carries public text, not a source-reference resolver. Citation identifiers therefore cannot become resolved URLs. Local file and `codex://` links remain subject to the Markdown renderer's URL policy; CAVE does not open arbitrary local files or invoke Codex host actions from retained text. Images and HTML follow the existing Markdown policy; raw HTML is never executed. These presentation directives are examples from the Codex host's message format, not separately guaranteed App Server item types.

## Protocol boundary

The [official App Server documentation](https://learn.chatgpt.com/docs/app-server) remains the external contract. The generated schema was inspected locally under ignored `output/` rather than committed as a second protocol authority.

All native adapters share executable selection: explicit `Cave:CodexCommand`, otherwise Desktop's `CODEX_CLI_PATH` when present, otherwise standalone PATH resolution. A present but invalid Desktop path reports a configuration error instead of silently selecting another installed version. These Desktop-launched native adapters do not require a separate CLI installation. The Desktop-provided backend and a standalone CLI may have different versions; a successful local handshake does not prove compatibility on another machine or with every experimental endpoint.

- `agentMessage.text` and `item/agentMessage/delta.delta` are strings in 0.157.0. Rich attribution is decoded from those strings by the frontend, rather than assuming an undocumented attributed-text JSON envelope.
- Main-task terminal activity includes interruption. A terminal observation releases the exact-task queue without creating a replacement task; subagent terminals do not release the main task.
- Isolated node questions use the documented ephemeral fork options and experimental capability handshake. They exclude inherited turns where required and defer inherited goal continuation before starting the single requested memo turn.
- Schema-known approval and elicitation requests are safely declined, tool calls are rejected, current-time requests use the authoritative clock, and unknown server requests receive an explicit unsupported-method error. CAVE does not expose remote approval or question-answer controls to the unauthenticated browser.
- Null or absent usage-summary metrics remain unavailable; genuine zero values remain zero. Multi-bucket quota windows are preferred where present.

The integration continues to separate observed activity from agent-declared scope. It does not promise automatic subagent attribution where hook metadata does not establish it. Experimental Codex APIs can change; focused schema and process-contract tests should accompany future upgrades.

## Verification and limits

The plugin hook definition now includes `Interrupt`. After installing an updated plugin through the canonical installer, review/trust its changed hook definition through Codex's `/hooks` flow. Source verification does not install the plugin or grant that trust.

The owning tests cover decoded presentation, malformed/streaming inputs, code preservation, unsafe URL/HTML handling, task identity, terminal activity, request responses, isolated fork options and nullable usage. Run `pwsh ./scripts/verify.ps1` for the canonical build, tests, plugin validation, CodeGraph and live health/snapshot checks.

Unit/process contract tests and a synthetic browser check do not establish that every Desktop capability is advertised on every host. Embedded sampling and messaging remain capability-detected. Authentication, interactive browser approvals, voice, and full parity with Codex's native rich UI remain outside this support claim. See [public readiness](../release/PUBLIC_READINESS.md) before publishing source or distributing a release.

### Desktop backend selection check (2026-10-15)

The local Desktop process supplied `CODEX_CLI_PATH` pointing to its bundled **0.162.0-alpha.17.2** backend, while the separately installed CLI reported **0.162.1**. Direct read-only probes of the Desktop backend accepted `initialize`, `account/usage/read`, and `account/rateLimits/read`. No task was resumed or changed. Seven focused tests cover Desktop selection without CLI PATH, a competing CLI, explicit override, invalid paths with visible unavailable results, and Windows command quoting. The full backend suite passed 220 tests; the unchanged frontend passed 202 tests and built successfully.

This verifies local executable selection and the observed local native calls, not the laptop's rejection or an installed update there. Full plugin validation remains blocked by the missing installed `plugin-creator/scripts/validate_plugin.py`; the temporary host health/snapshot verification command was rejected by automatic approval review as blocked by policy. Do not bypass canonical validation or claim that the laptop has been repaired.

### Native process exit check (2026-10-17)

Windows CI exposed caller cancellation returning while the owned `cmd.exe` still held its command file. The shared process owner now requests tree termination and awaits the owned App Server's exit with an independent five-second cleanup deadline. Goal, usage, telemetry and both turn paths use that contract. A cleanup timeout remains an explicit failure; telemetry preserves collected values and existing warnings while adding a safe cleanup diagnostic. Regression cases cover running and naturally exited command processes, repeated cleanup, exclusive command-file access after return, caller cancellation, and sanitized telemetry failure/cache behavior. The combined source passes 247 backend tests and 212 frontend tests/build locally; these checks do not establish an installed laptop repair.

## Configured models and statistics (2026-10-15)

Verified against installed CLI **0.162.1**, its generated experimental TypeScript protocol under ignored `output/codex-protocol`, process-contract tests, and a live exact-task read-only probe:

- `thread/read` with `includeTurns: false` returns exact `id`, nullable `parentThreadId`, `model` and `reasoningEffort`. These are current or latest persisted configured settings, explicitly not per-turn execution telemetry. No parent model is inherited.
- `thread/turns/list` with `itemsView: notLoaded` supplies nullable recorded durations without conversation items. CAVE reads at most four 50-turn pages, deduplicates turn IDs/cursors, and reports partial coverage.
- `account/usage/read` accepts `threadId` and may return matching `threadUsage.groups` with individually nullable input, cached-input, output and total tokens grouped by model/effort. These are source estimates. The live task returned null thread usage; support does not establish availability on every account or billing route.
- Independent metadata, token and duration reads use one bounded 12-second batch and exact-thread cache without task-control requests, transcript ingestion or hidden reasoning.
- `GET /api/statistics` and app-private `cave_get_statistics` return schema 1 with opaque task keys, coverage and synthetic-demo labels. The installed viewer requires protocol **3**. Optional configured metadata in existing Light/activity feeds remains additive within schema 1.

Lifetime statistics mean retained CAVE-observed history. Native historical token groups keep their source model attribution; historical duration has no model attribution. Prospective time under configured settings is separately sampled; gaps/changes are unattributed. Account quotas and totals are never distributed across agents or projects.

Local installation preflight on 2026-10-15 stops because the installed Codex setup lacks the `plugin-creator` helpers used by the canonical installer (`update_plugin_cachebuster.py`, `read_marketplace_name.py`, and `validate_plugin.py`). Repository metadata, published binaries and runtime checks can be verified independently; they do not establish a completed plugin installation. Restore the canonical development tooling before using `scripts/install-local-plugin.ps1`; do not hand-copy deployed plugin state or bypass the missing validation.

# CAVE Agent Flow Light v1

A read-only JSON feed and portable display resources for native apps, companion displays, and embedded devices. No browser, semantic graph, or JavaScript runtime is required by the API or the C client.

## Connect

1. Choose a workspace from `GET /api/workspaces`; use its opaque `workspaceId`. That catalog may contain local paths, so only retain the ID and display name your app needs.
2. Poll `GET /api/agent-flow?workspace=<id>` every two seconds, with one request in flight. Allow 15 seconds for the first native goal lookup. The goal adapter caches exact-task reads for 15 seconds; polling more quickly does not make the native goal fresher.
3. Download this pack at `GET /api/agent-flow/resources` (`cave-agent-flow-light-v1.zip`). The schema is also available at `GET /api/agent-flow/schema`.

Use the existing CAVE host on loopback or an operator-approved private network. These read endpoints inherit CAVE's unauthenticated trusted-network mode. They do not require changes to host binding or network policy.

| Resource | Purpose |
| --- | --- |
| `schema.json` | JSON Schema 2020-12 for the version 1 response |
| `example.json` | Synthetic fixture; safe for offline UI development |
| `palette.json` | Static colors; pair each color with a visible state/phase label |
| `assets/` | Main-agent/subagent SVGs, PNGs, and monochrome C bitmap data |
| `examples/poll.mjs` | Small native Node polling client |
| `embedded/` | Fixed-buffer portable C parser, polling example, tests, and STM32 integration notes |
| `LICENSE` | MIT terms included in the downloaded pack |

## Display contract

- Check `schemaVersion === 1`. Accept additional properties in that version; reject an unsupported version visibly.
- Feed responses use `Cache-Control: no-store`; do not add a cache that retains shared objectives across privacy changes.
- `sourceMode` distinguishes `Live` from explicitly synthetic `Demo` data.
- There are at most 32 agent rows. `totalAgentCount` reports the pre-limit count, so show an overflow count when necessary. Ordering is active first, then newest evidence, with stable identity ties.
- Use `agentId` as the row key. It is a SHA256 correlation key, **not** a Codex task ID. Do not key rows by `displayName`: single first names intentionally may repeat. Names match CAVE's browser views; the name pool is 48 female and 16 male names. The main agent is labeled `Main agent`.
- When `parentAgentId` is present, place the subagent below the row with that opaque key. An absent or unmatched parent remains ungrouped; do not invent a relationship. The browser Light view can aggregate the individual workspace feeds for an all-projects display. Native clients continue selecting one workspace per request.
- Show `currentFocus` as declared intent when present. It is populated only from a declared summary. Otherwise show `summary` with its `summaryEvidence` as an observed action or unknown provenance. A tool action is not a subgoal. CAVE does not infer child-agent goal membership.
- `lastObservedActivity` carries the latest meaningful tool/work action separately from declared focus. Terminal lifecycle hooks do not describe what was completed. Hide finished rows within ten minutes of `updatedAtUtc`; a client may let the operator dismiss them sooner.
- `generatedAtUtc` is the activity projection read time; `sourceUpdatedAtUtc` is the latest actual activity evidence. They are separate from `mainGoal.retrievedAtUtc`. A successful poll does not prove that an agent is actively working: use its `state` and `phase`.
- Clear goal text when privacy or task availability changes. Do not keep a previous objective displayed as current after a failed response. Network failure should produce an explicit disconnected/stale display.

| `mainGoal.status` | Meaning |
| --- | --- |
| `Private` | Conversation sharing is disabled; goal is null |
| `Unbound` | Sharing is enabled but no exact workspace task is bound |
| `Unavailable` | The exact native goal cannot be published; goal is null |
| `Ready`, goal null | The exact task was read and has no native goal |
| `Ready`, goal present | Shared native main goal, independently of agent phase |

The feed never returns chat messages, a graph, Git state, root paths, native task IDs, account identity, or per-agent token attribution. Declared focus is activity metadata and follows the existing activity contract. Main-goal text additionally requires conversation sharing and an unchanged exact task binding after the lookup.

## Bounds and time

Server text limits are in UTF-16 code units: role 64, workspace name/summary/focus 160, objective 2048. The server preserves complete surrogate pairs. `isTruncated` identifies an abbreviated objective. JSON Schema measures Unicode characters, so these schema bounds are an upper bound; the server's UTF-16 limits are stricter for supplementary characters. A decoded UTF-8 string can require up to three bytes per UTF-16 unit. The JSON body may be larger because Unicode can be escaped as `\uXXXX` and summary/focus may both appear. A 256 KiB body buffer accommodates the bounded v1 feed.

UTC timestamps are RFC 3339 strings. Native goal `createdAt` and `updatedAt` are raw signed 64-bit integers: their units and sign semantics are not assumed. Token/time counters are signed 64-bit nonnegative integers; an optional token budget is positive. Preserve null as unknown/unset and zero as a valid measurement. JavaScript consumers needing exact counters or raw timestamps outside the safe integer range must use a parser that preserves integer precision.

| HTTP status | Client action |
| --- | --- |
| 200 | Validate and atomically replace the current display |
| 400 | Supply a workspace ID |
| 404 | Refresh selection; this ID is not registered |
| 410 | Display unavailable; the registered directory is missing |
| Network/5xx | Mark disconnected; retry with a bounded delay and clear current private text |

For optional timeline details, CAVE's separate `GET /api/recent-activity?workspace=<id>&minutes=15&pageSize=200` exposes bounded activity evidence. It is a different contract, with its own IDs and paging; the Light row key cannot be passed as a native task identifier.

## STM32N6 handoff

Start with [embedded/README.md](embedded/README.md). Copy the C client and bitmap header into the n6 application, adapt the transport callback to its existing HTTP stack, and map a validated snapshot to its display framework. The pack does not install drivers, change the board's networking, or flash firmware. The C parser is verified on a host compiler; board networking, display integration, and on-device execution require verification in the n6 project.

ST documents Ethernet and display hardware on the [STM32N6570-DK](https://www.st.com/en/evaluation-tools/stm32n6570-dk) and supplies the [STM32CubeN6 package](https://github.com/STMicroelectronics/STM32CubeN6). Those are integration references; this pack makes no assumptions about n6's selected RTOS or networking library.

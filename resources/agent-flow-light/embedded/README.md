# Portable C/C++ consumer

This example consumes the pack's schema-version-1 `GET /api/agent-flow?workspace=<opaque-id>` feed. It is independent of board drivers, networking libraries, graphics libraries, RTOS choice, and filesystem support. The intended integration target is an existing STM32N6 Discovery application. No n6 project files, firmware deployment, or hardware configuration are included or changed.

## Files and integration

- Compile `cave_agent_flow.c` and `poll_example.c` as C11; include their headers from C or C++17. There are no third-party or heap dependencies.
- Supply `cave_flow_http_get` using your existing HTTP client. Configure its host/port in that transport owner. The supplied path uses the opaque catalog workspace identifier and percent-encodes it; a filesystem path is never required.
- The callback performs one bounded GET. It supplies a fully decoded response body, length, and HTTP status. Decode chunked transfer encoding in the transport; reject unsupported compression or decode it within your own bounded implementation. Return `CAVE_FLOW_TOO_LARGE` if the body exceeds capacity, `CAVE_FLOW_CANCELLED` on cancellation, or `CAVE_FLOW_TRANSPORT_ERROR` on connection/timeout failure. Never return a clipped body as success.
- Call `cave_flow_example_init` once, then `cave_flow_example_tick` from one owner task using your existing monotonic millisecond clock. For example, use a 2,000 ms interval. Each tick performs at most one poll; unsigned subtraction handles clock wraparound. The HTTP callback must enforce its own deadline and honor the cancellation flag. Do not call the example concurrently or from an interrupt. Platform synchronization owns cancellation signaling; `volatile` alone does not provide cross-thread synchronization.
- Adapt `render` and `show_status` to your existing display owner. The example does not start a thread, drive Ethernet/Wi-Fi, initialize a screen, create an LVGL object, or select a firmware deployment method.

```c
/* Allocate once in a region approved by your application's memory/linker plan. */
static cave_flow_example flow;

/* Existing adapters implement these callback signatures. */
extern cave_flow_result board_http_get(void *, const char *, char *, size_t,
    size_t *, unsigned *, const volatile bool *);
extern void board_render_flow(void *, const cave_flow_snapshot *);
extern void board_show_flow_status(void *, cave_flow_result);

void app_start_flow(const char *workspace_id)
{
    cave_flow_result result = cave_flow_example_init(&flow, board_http_get, NULL,
        workspace_id, board_render_flow, board_show_flow_status, NULL, 2000);
    if (result != CAVE_FLOW_OK) board_show_flow_status(NULL, result);
}

void app_tick_flow(uint32_t monotonic_ms)
{
    (void)cave_flow_example_tick(&flow, monotonic_ms);
}
```

## Display contract

Render the bounded rows using `display_name`; `agent_id` is only an external correlation key. CAVE supplies the canonical name, so the firmware must not recompute or invent it. Preserve `evidence`, `summary_evidence`, and `focus_evidence` labels. A declared focus is not observed behavior or a structured native subgoal.

Render a goal only when `main_goal.status == CAVE_FLOW_GOAL_READY && main_goal.has_goal`. `Ready` with `has_goal == false` means the exact task has no goal. `Private`, `Unbound`, and `Unavailable` are distinct source states and have no goal. The parser rejects a response exposing a goal under those states.

**On any failed poll, `show_status` must immediately clear previously displayed goal text and mark retained activity stale/unavailable.** The published snapshot stays unchanged for atomicity; that does not authorize rendering old shared text as current. Do not fabricate `Private` or any other backend status when disconnected. Treat cancellation as an intentional stop and clear the display as appropriate. A later successful private/no-goal response replaces and clears the old snapshot.

Null is represented by presence flags or the explicit no-phase/no-evidence enums. A supplied token budget must be positive; a null budget is unavailable, while zero token/time counters are valid reported values. `has_summary == false` differs from an empty supplied summary. Source/generated timestamps stay separate. `created_at` and `updated_at` in the native goal are raw signed integers with undocumented units; do not convert them to wall-clock time or reject negative raw timestamps. Per-agent tokens, chat text, file paths, graphs, and timeline history are outside this feed.

## Bounds and memory

All storage is caller-owned. No library or polling-example function allocates heap memory.

| Storage | Default capacity |
| --- | ---: |
| HTTP response body | 262,144 bytes (256 KiB) |
| One snapshot, measured by the host test | 56,416 bytes |
| Example object: body + scratch + published + scheduling/callbacks, host measurement | 375,040 bytes |
| Agents | 32 |
| Goal UTF-8 text, including NUL | 6,145 bytes |
| Each 160 UTF-16-unit text field, including NUL | 481 bytes |
| Each 32 UTF-16-unit display name, including NUL | 97 bytes |
| Each 512 UTF-16-unit diagnostic, including NUL | 1,537 bytes |
| Unknown JSON nesting | 32 containers |

These object sizes were measured on the Windows x64 MSVC host ABI; alignment and `size_t` may differ on the target. Use `sizeof` and your target linker/map report before placement. Keep the large example/snapshots in caller-owned static/task storage, not on a small task stack. The parser uses bounded stack recursion only while validating unknown fields; measure stack high-water usage with the chosen target compiler and worst-depth fixture. The HTTP stack, TLS, graphics, DMA buffers, and their memory are additional application-owned costs.

The 256 KiB body bound includes JSON escaping overhead: a UTF-16 source character may take six ASCII bytes as `\uXXXX`. Published text expands to UTF-8 without truncation. You may provide a smaller body buffer to `cave_flow_poll`; an otherwise valid larger feed will then report `CAVE_FLOW_TOO_LARGE`. Do not claim that smaller capacity supports the maximum schema payload.

## Parser behavior

The single strict streaming parser validates object/array grammar, escaped characters, UTF-8, surrogate pairs, signed 64-bit integer overflow, counter/budget ranges, timestamps, required/duplicate known fields, enum/version support, text bounds, and privacy states. Focus is exactly the declared nonnull summary with matching provenance; an empty declared summary/focus stays empty. The polling boundary also verifies the returned workspace matches the requested opaque identifier before publishing. Valid unknown fields are skipped, including nested objects/arrays. Malformed unknown fields are rejected. Known integer fields must use the plain decimal representation emitted by CAVE; scientific/fractional notation is only accepted in unknown JSON values. The response must be complete UTF-8 JSON without a BOM or trailing data.

Embedded NUL in a published string is rejected because a C string cannot preserve its meaning. Unpaired Unicode surrogates are rejected; valid surrogate pairs decode to UTF-8. Arrays/text/body/number bounds return explicit failures. The parser never invents default provenance, changes native timestamps, truncates a row, or substitutes another task. `scratch` is disposable; `published` changes only after the complete response validates. Supply distinct, nonoverlapping input/scratch/published buffers.

## Native verification

From the CAVE repository:

```powershell
pwsh -NoProfile -File resources/agent-flow-light/embedded/test.ps1
```

From an extracted portable pack:

```powershell
pwsh -NoProfile -File embedded/test.ps1
```

The driver discovers an installed compiler; it installs nothing. On Windows it uses MSVC from the active developer shell or locates a complete toolchain with Visual Studio's `vswhere`, using `/W4 /WX`. Its MSVC batch driver explicitly requires ASCII source/output/toolchain paths and fails visibly otherwise; move an extracted pack to an ASCII path or select GCC/Clang rather than allowing path characters to be changed. GCC/Clang use `-Wall -Wextra -Werror -pedantic`. C++ compilation and linking verify the C API. Artifacts go to the repository's ignored `output/agent-flow-light-native`, or to the extracted pack's own `output/agent-flow-light-native`; none are emitted into `embedded` or an ancestor outside the pack.

Tests cover the full three-agent synthetic fixture, the pack-root `example.json`, escaped Unicode, null/zero/empty distinctions, all goal source states, unknown fields, missing/duplicate fields, malformed JSON/UTF-8/timestamps, unsupported versions/enums, privacy violations, overflow/depth/array/text/body limits, HTTP failures, cancellation, URL encoding, atomic publication, polling cadence and clock wraparound. Host compilation/tests establish portable parser behavior. STM32N6 compilation, actual network/display integration, target stack/memory measurements, and board operation still require the owning firmware project; they were not performed here.

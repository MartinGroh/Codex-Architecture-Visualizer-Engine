#ifndef CAVE_FLOW_POLL_EXAMPLE_H
#define CAVE_FLOW_POLL_EXAMPLE_H
#include "cave_agent_flow.h"
#ifdef __cplusplus
extern "C" {
#endif

/* Display callbacks adapt your existing graphics/UI owner; no board driver is
 * supplied. Never render objective unless has_goal is true and status Ready.
 * A failure callback must clear previously displayed goal text and mark the
 * prior activity stale/unavailable. Do not synthesize Private on disconnection;
 * cancellation is an intentional stop, not a network failure. */
typedef void (*cave_flow_render)(void *context, const cave_flow_snapshot *snapshot);
typedef void (*cave_flow_show_status)(void *context, cave_flow_result result);
typedef struct {
    cave_flow_http_get http_get;
    void *transport_context;
    const char *workspace_id;
    cave_flow_render render;
    cave_flow_show_status show_status;
    void *display_context;
    bool has_polled;
    uint32_t last_poll_ms;
    uint32_t interval_ms;
    volatile bool cancelled;
    char body[CAVE_FLOW_MAX_RESPONSE_BYTES];
    cave_flow_snapshot scratch;
    cave_flow_snapshot published;
} cave_flow_example;

/* Initialize a caller-owned static/task object. interval_ms must be 1..INT32_MAX.
 * Run tick from one owner task; callbacks may block only within your transport's
 * configured deadline. Do not call tick concurrently or from an interrupt. */
cave_flow_result cave_flow_example_init(cave_flow_example *example,
    cave_flow_http_get http_get, void *transport_context, const char *workspace_id,
    cave_flow_render render, cave_flow_show_status show_status, void *display_context,
    uint32_t interval_ms);
/* Caller supplies its monotonic uint32 millisecond clock. Wraparound is handled
 * by unsigned subtraction. Return true only when this tick attempted a poll. */
bool cave_flow_example_tick(cave_flow_example *example, uint32_t now_ms);
#ifdef __cplusplus
}
#endif
#endif

#include "poll_example.h"
#include <limits.h>
#include <string.h>

cave_flow_result cave_flow_example_init(cave_flow_example *e,
    cave_flow_http_get http_get, void *transport_context, const char *workspace_id,
    cave_flow_render render, cave_flow_show_status show_status, void *display_context,
    uint32_t interval_ms) {
    if (e == NULL || http_get == NULL || workspace_id == NULL || render == NULL || show_status == NULL
        || interval_ms == 0 || interval_ms > INT32_MAX) return CAVE_FLOW_BAD_ARGUMENT;
    memset(e, 0, sizeof(*e));
    e->http_get = http_get; e->transport_context = transport_context; e->workspace_id = workspace_id;
    e->render = render; e->show_status = show_status; e->display_context = display_context; e->interval_ms = interval_ms;
    return CAVE_FLOW_OK;
}
bool cave_flow_example_tick(cave_flow_example *e, uint32_t now_ms) {
    cave_flow_result result;
    if (e == NULL || e->http_get == NULL || e->cancelled) return false;
    if (e->has_polled && (uint32_t)(now_ms - e->last_poll_ms) < e->interval_ms) return false;
    e->has_polled = true; e->last_poll_ms = now_ms;
    result = cave_flow_poll(e->http_get,e->transport_context,e->workspace_id,e->body,sizeof(e->body),&e->scratch,&e->published,&e->cancelled);
    if (result == CAVE_FLOW_OK) e->render(e->display_context,&e->published);
    else e->show_status(e->display_context,result);
    return true;
}

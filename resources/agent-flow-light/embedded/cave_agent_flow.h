#ifndef CAVE_AGENT_FLOW_H
#define CAVE_AGENT_FLOW_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Schema 1 bounds, including UTF-8 expansion and a terminating NUL.
 * Storage is supplied by the caller; these APIs never allocate memory. */
#define CAVE_FLOW_MAX_AGENTS 32u
#define CAVE_FLOW_MAX_RESPONSE_BYTES 262144u
#define CAVE_FLOW_TEXT_BYTES 481u
#define CAVE_FLOW_NAME_BYTES 97u
#define CAVE_FLOW_ROLE_BYTES 193u
#define CAVE_FLOW_GOAL_BYTES 6145u
#define CAVE_FLOW_ERROR_BYTES 1537u
#define CAVE_FLOW_ID_BYTES 385u
#define CAVE_FLOW_DATE_BYTES 49u

typedef enum {
    CAVE_FLOW_OK = 0, CAVE_FLOW_BAD_ARGUMENT, CAVE_FLOW_MALFORMED,
    CAVE_FLOW_UNSUPPORTED, CAVE_FLOW_TOO_LARGE, CAVE_FLOW_TRANSPORT_ERROR,
    CAVE_FLOW_CANCELLED, CAVE_FLOW_HTTP_ERROR, CAVE_FLOW_WORKSPACE_MISMATCH
} cave_flow_result;
typedef enum { CAVE_FLOW_LIVE, CAVE_FLOW_DEMO } cave_flow_mode;
typedef enum { CAVE_FLOW_UNOBSERVED, CAVE_FLOW_READY, CAVE_FLOW_DEGRADED } cave_flow_source;
typedef enum { CAVE_FLOW_PLANNED, CAVE_FLOW_ACTIVE, CAVE_FLOW_IDLE, CAVE_FLOW_COMPLETED } cave_flow_state;
typedef enum { CAVE_FLOW_NO_PHASE, CAVE_FLOW_THINKING, CAVE_FLOW_READING, CAVE_FLOW_EDITING, CAVE_FLOW_VALIDATING, CAVE_FLOW_WORKING } cave_flow_phase;
typedef enum { CAVE_FLOW_NO_EVIDENCE, CAVE_FLOW_OBSERVED, CAVE_FLOW_DECLARED } cave_flow_evidence;
typedef enum { CAVE_FLOW_GOAL_PRIVATE, CAVE_FLOW_GOAL_UNBOUND, CAVE_FLOW_GOAL_READY, CAVE_FLOW_GOAL_UNAVAILABLE } cave_flow_goal_source;
typedef enum { CAVE_FLOW_GOAL_ACTIVE, CAVE_FLOW_GOAL_PAUSED, CAVE_FLOW_GOAL_BLOCKED, CAVE_FLOW_GOAL_USAGE_LIMITED, CAVE_FLOW_GOAL_BUDGET_LIMITED, CAVE_FLOW_GOAL_COMPLETE } cave_flow_goal_state;

/* Presence flags preserve null separately from empty text and numeric zero. */
typedef struct {
    char agent_id[65];
    bool has_parent_agent_id;
    char parent_agent_id[65];
    bool has_last_observed_activity;
    char last_observed_activity[CAVE_FLOW_TEXT_BYTES];
    char display_name[CAVE_FLOW_NAME_BYTES];
    char agent_type[CAVE_FLOW_ROLE_BYTES];
    bool is_subagent;
    cave_flow_state state;
    cave_flow_phase phase;
    bool has_summary;
    char summary[CAVE_FLOW_TEXT_BYTES];
    bool has_current_focus;
    char current_focus[CAVE_FLOW_TEXT_BYTES];
    cave_flow_evidence focus_evidence;
    char started_at_utc[CAVE_FLOW_DATE_BYTES];
    char updated_at_utc[CAVE_FLOW_DATE_BYTES];
    cave_flow_evidence evidence;
    cave_flow_evidence summary_evidence;
} cave_flow_agent;

typedef struct {
    char objective[CAVE_FLOW_GOAL_BYTES];
    bool is_truncated;
    cave_flow_goal_state status;
    bool has_token_budget;
    int64_t token_budget;
    int64_t tokens_used;
    int64_t time_used_seconds;
    /* Native timestamps have undocumented units; never interpret as Unix time. */
    int64_t created_at;
    int64_t updated_at;
} cave_flow_goal;

typedef struct {
    cave_flow_goal_source status;
    bool has_goal;
    cave_flow_goal goal;
    bool has_retrieved_at;
    char retrieved_at_utc[CAVE_FLOW_DATE_BYTES];
    bool has_error;
    char error[CAVE_FLOW_ERROR_BYTES];
} cave_flow_main_goal;

typedef struct {
    uint32_t schema_version;
    char workspace_id[CAVE_FLOW_ID_BYTES];
    char workspace_name[CAVE_FLOW_TEXT_BYTES];
    cave_flow_mode source_mode;
    char generated_at_utc[CAVE_FLOW_DATE_BYTES];
    bool has_source_updated_at;
    char source_updated_at_utc[CAVE_FLOW_DATE_BYTES];
    cave_flow_source source_status;
    bool has_error;
    char error[CAVE_FLOW_ERROR_BYTES];
    uint32_t total_agent_count;
    size_t agent_count;
    cave_flow_agent agents[CAVE_FLOW_MAX_AGENTS];
    cave_flow_main_goal main_goal;
} cave_flow_snapshot;

/* Parse one complete UTF-8 JSON response. Unknown fields are validated/skipped.
 * All schema-1 fields are required, including nullable fields. Buffers must be
 * distinct: scratch is disposable; published is changed only on success.
 * Oversized strings/arrays/response, invalid UTF-8, duplicate known keys,
 * unsupported versions/enums, malformed JSON and privacy violations fail.
 * No truncation, fallback state, HTTP operation, or timestamp inference occurs. */
cave_flow_result cave_flow_parse(const char *json, size_t length,
    cave_flow_snapshot *scratch, cave_flow_snapshot *published);

/* Existing networking code owns connections, TLS where configured, HTTP status,
 * chunked transfer decoding, timeouts, and cancellation. It must report a too-
 * large body instead of returning a clipped body as success. The body need not
 * be NUL terminated. Context and cancellation flag remain caller-owned. */
typedef cave_flow_result (*cave_flow_http_get)(void *context, const char *path,
    char *body, size_t capacity, size_t *body_length, unsigned *http_status,
    const volatile bool *cancelled);

/* Perform exactly one GET through the supplied transport, validate the exact
 * requested workspace identifier, then atomically publish the parsed response.
 * A failed/cancelled poll leaves published untouched; the caller must visibly
 * mark any retained display as stale rather than presenting it as a fresh feed. */
cave_flow_result cave_flow_poll(cave_flow_http_get http_get, void *context,
    const char *workspace_id, char *body, size_t capacity,
    cave_flow_snapshot *scratch, cave_flow_snapshot *published,
    const volatile bool *cancelled);

/* Stable diagnostic names, suitable for an unavailable/stale display label. */
const char *cave_flow_result_name(cave_flow_result result);

#ifdef __cplusplus
}
#endif
#endif

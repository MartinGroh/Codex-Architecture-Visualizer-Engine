#include "cave_agent_flow.h"
#include <limits.h>
#include <string.h>

/* One strict streaming parser; no token tree, heap, or second permissive path.
 * Depth bounds protect the native/MCU stack even inside unknown JSON fields. */
typedef struct { const unsigned char *s; size_t n, p; unsigned depth; cave_flow_result error; } reader;
static bool fail(reader *r, cave_flow_result error) { r->error = error; return false; }
static void space(reader *r) { while (r->p < r->n && (r->s[r->p] == ' ' || r->s[r->p] == '\t' || r->s[r->p] == '\n' || r->s[r->p] == '\r')) ++r->p; }
static bool take(reader *r, unsigned char c) { space(r); if (r->p >= r->n || r->s[r->p] != c) return fail(r, CAVE_FLOW_MALFORMED); ++r->p; return true; }
static bool literal(reader *r, const char *word) { size_t n = strlen(word); space(r); if (n > r->n - r->p || memcmp(r->s + r->p, word, n) != 0) return fail(r, CAVE_FLOW_MALFORMED); r->p += n; return true; }
static bool is_null(reader *r) { space(r); return r->p < r->n && r->s[r->p] == 'n'; }
static int hex(unsigned char c) { if (c >= '0' && c <= '9') return c - '0'; if (c >= 'a' && c <= 'f') return c - 'a' + 10; if (c >= 'A' && c <= 'F') return c - 'A' + 10; return -1; }
static bool hex4(reader *r, uint32_t *value) {
    unsigned i; *value = 0;
    for (i = 0; i < 4; ++i) { int h; if (r->p >= r->n || (h = hex(r->s[r->p++])) < 0) return fail(r, CAVE_FLOW_MALFORMED); *value = (*value << 4) | (uint32_t)h; }
    return true;
}
static bool string_value(reader *r, char *out, size_t cap, size_t max_units, bool *overflow) {
    size_t used = 0, units = 0; bool over = false;
    if (!take(r, '"')) return false;
    while (r->p < r->n) {
        uint32_t cp; unsigned char bytes[4]; size_t count = 0, i;
        unsigned char c = r->s[r->p++];
        if (c == '"') { if (out != NULL && cap > 0) out[used < cap ? used : cap - 1] = '\0'; if (overflow != NULL) *overflow = over; return true; }
        if (c < 0x20) return fail(r, CAVE_FLOW_MALFORMED);
        if (c == '\\') {
            if (r->p >= r->n) return fail(r, CAVE_FLOW_MALFORMED);
            c = r->s[r->p++];
            if (c == 'u') {
                if (!hex4(r, &cp)) return false;
                if (cp >= 0xd800 && cp <= 0xdbff) {
                    uint32_t low;
                    if (r->n - r->p < 2 || r->s[r->p] != '\\' || r->s[r->p + 1] != 'u') return fail(r, CAVE_FLOW_MALFORMED);
                    r->p += 2;
                    if (!hex4(r, &low) || low < 0xdc00 || low > 0xdfff) return fail(r, CAVE_FLOW_MALFORMED);
                    cp = 0x10000u + ((cp - 0xd800u) << 10) + low - 0xdc00u;
                } else if (cp >= 0xdc00 && cp <= 0xdfff) return fail(r, CAVE_FLOW_MALFORMED);
            } else {
                switch (c) { case '"': case '\\': case '/': cp = c; break; case 'b': cp = 8; break; case 'f': cp = 12; break; case 'n': cp = 10; break; case 'r': cp = 13; break; case 't': cp = 9; break; default: return fail(r, CAVE_FLOW_MALFORMED); }
            }
        } else if (c < 0x80) cp = c;
        else {
            uint32_t minimum; size_t more;
            if (c >= 0xc2 && c <= 0xdf) { cp = c & 0x1fu; more = 1; minimum = 0x80; }
            else if (c >= 0xe0 && c <= 0xef) { cp = c & 0x0fu; more = 2; minimum = 0x800; }
            else if (c >= 0xf0 && c <= 0xf4) { cp = c & 7u; more = 3; minimum = 0x10000; }
            else return fail(r, CAVE_FLOW_MALFORMED);
            for (i = 0; i < more; ++i) { unsigned char next; if (r->p >= r->n || ((next = r->s[r->p++]) & 0xc0u) != 0x80u) return fail(r, CAVE_FLOW_MALFORMED); cp = (cp << 6) | (next & 0x3fu); }
            if (cp < minimum || cp > 0x10ffff || (cp >= 0xd800 && cp <= 0xdfff)) return fail(r, CAVE_FLOW_MALFORMED);
        }
        units += cp > 0xffff ? 2u : 1u;
        if (max_units != 0 && units > max_units) return fail(r, CAVE_FLOW_TOO_LARGE);
        /* Embedded NUL cannot be represented faithfully by published C strings. */
        if (cp == 0 && out != NULL) return fail(r, CAVE_FLOW_MALFORMED);
        if (cp < 0x80) { bytes[0] = (unsigned char)cp; count = 1; }
        else if (cp < 0x800) { bytes[0] = (unsigned char)(0xc0u | (cp >> 6)); bytes[1] = (unsigned char)(0x80u | (cp & 0x3fu)); count = 2; }
        else if (cp < 0x10000) { bytes[0] = (unsigned char)(0xe0u | (cp >> 12)); bytes[1] = (unsigned char)(0x80u | ((cp >> 6) & 0x3fu)); bytes[2] = (unsigned char)(0x80u | (cp & 0x3fu)); count = 3; }
        else { bytes[0] = (unsigned char)(0xf0u | (cp >> 18)); bytes[1] = (unsigned char)(0x80u | ((cp >> 12) & 0x3fu)); bytes[2] = (unsigned char)(0x80u | ((cp >> 6) & 0x3fu)); bytes[3] = (unsigned char)(0x80u | (cp & 0x3fu)); count = 4; }
        if (out != NULL) { if (used + count >= cap) over = true; else if (!over) { memcpy(out + used, bytes, count); used += count; } }
    }
    return fail(r, CAVE_FLOW_MALFORMED);
}
static bool text(reader *r, char *out, size_t cap, size_t max_units, bool *present) {
    bool overflow = false;
    if (present != NULL) { *present = !is_null(r); if (!*present) { out[0] = '\0'; return literal(r, "null"); } }
    if (!string_value(r, out, cap, max_units, &overflow)) return false;
    return !overflow || fail(r, CAVE_FLOW_TOO_LARGE);
}
static bool boolean(reader *r, bool *out) { space(r); if (r->p >= r->n) return fail(r, CAVE_FLOW_MALFORMED); *out = r->s[r->p] == 't'; return literal(r, *out ? "true" : "false"); }
static bool digit(unsigned char c) { return c >= '0' && c <= '9'; }
static bool number_span(reader *r, size_t *start, bool *integer) {
    space(r); *start = r->p; *integer = true;
    if (r->p < r->n && r->s[r->p] == '-') ++r->p;
    if (r->p >= r->n) return fail(r, CAVE_FLOW_MALFORMED);
    if (r->s[r->p] == '0') ++r->p;
    else if (r->s[r->p] >= '1' && r->s[r->p] <= '9') { do { ++r->p; } while (r->p < r->n && digit(r->s[r->p])); }
    else return fail(r, CAVE_FLOW_MALFORMED);
    if (r->p < r->n && r->s[r->p] == '.') { *integer = false; ++r->p; if (r->p >= r->n || !digit(r->s[r->p])) return fail(r, CAVE_FLOW_MALFORMED); while (r->p < r->n && digit(r->s[r->p])) ++r->p; }
    if (r->p < r->n && (r->s[r->p] == 'e' || r->s[r->p] == 'E')) { *integer = false; ++r->p; if (r->p < r->n && (r->s[r->p] == '+' || r->s[r->p] == '-')) ++r->p; if (r->p >= r->n || !digit(r->s[r->p])) return fail(r, CAVE_FLOW_MALFORMED); while (r->p < r->n && digit(r->s[r->p])) ++r->p; }
    if (r->p < r->n && r->s[r->p] != ',' && r->s[r->p] != ']' && r->s[r->p] != '}' && r->s[r->p] != ' ' && r->s[r->p] != '\t' && r->s[r->p] != '\r' && r->s[r->p] != '\n') return fail(r, CAVE_FLOW_MALFORMED);
    return true;
}
static bool integer64(reader *r, int64_t *out, bool *present) {
    size_t start, i; bool integral, negative; uint64_t value = 0, limit;
    if (present != NULL) { *present = !is_null(r); if (!*present) return literal(r, "null"); }
    if (!number_span(r, &start, &integral) || !integral) return fail(r, CAVE_FLOW_MALFORMED);
    negative = r->s[start] == '-'; i = start + (negative ? 1u : 0u);
    limit = negative ? ((uint64_t)INT64_MAX + 1u) : (uint64_t)INT64_MAX;
    for (; i < r->p; ++i) { unsigned d = (unsigned)(r->s[i] - '0'); if (value > (limit - d) / 10u) return fail(r, CAVE_FLOW_TOO_LARGE); value = value * 10u + d; }
    *out = negative ? (value == (uint64_t)INT64_MAX + 1u ? INT64_MIN : -(int64_t)value) : (int64_t)value;
    return true;
}
static bool unsigned32(reader *r, uint32_t *out) { int64_t value; if (!integer64(r, &value, NULL)) return false; if (value < 0 || (uint64_t)value > UINT32_MAX) return fail(r, CAVE_FLOW_TOO_LARGE); *out = (uint32_t)value; return true; }

static bool skip(reader *r) {
    unsigned char c; space(r); if (r->p >= r->n) return fail(r, CAVE_FLOW_MALFORMED); c = r->s[r->p];
    if (c == '"') return string_value(r, NULL, 0, 0, NULL);
    if (c == 't') return literal(r, "true");
    if (c == 'f') return literal(r, "false");
    if (c == 'n') return literal(r, "null");
    if (c == '{' || c == '[') {
        unsigned char close = c == '{' ? '}' : ']'; bool object = c == '{';
        if (++r->depth > 32) return fail(r, CAVE_FLOW_TOO_LARGE);
        ++r->p;
        space(r);
        if (r->p < r->n && r->s[r->p] == close) { ++r->p; --r->depth; return true; }
        for (;;) { if (object && (!string_value(r, NULL, 0, 0, NULL) || !take(r, ':'))) return false; if (!skip(r)) return false; space(r); if (r->p < r->n && r->s[r->p] == close) { ++r->p; --r->depth; return true; } if (!take(r, ',')) return false; }
    }
    { size_t start; bool integral; return number_span(r, &start, &integral); }
}
static bool enumeration(reader *r, const char *const *names, size_t count, int *out, bool nullable) {
    char value[32]; size_t i;
    if (nullable && is_null(r)) { *out = 0; return literal(r, "null"); }
    if (!text(r, value, sizeof(value), 31, NULL)) return false;
    for (i = nullable ? 1u : 0u; i < count; ++i) if (strcmp(value, names[i]) == 0) { *out = (int)i; return true; }
    return fail(r, CAVE_FLOW_UNSUPPORTED);
}
static const char *const phases[] = { "", "Thinking", "Reading", "Editing", "Validating", "Working" };
static const char *const states[] = { "Planned", "Active", "Idle", "Completed" };
static const char *const evidence[] = { "", "Observed", "Declared" };
static const char *const goal_sources[] = { "Private", "Unbound", "Ready", "Unavailable" };
static const char *const goal_states[] = { "Active", "Paused", "Blocked", "UsageLimited", "BudgetLimited", "Complete" };
static const char *const modes[] = { "Live", "Demo" };
static const char *const sources[] = { "Unobserved", "Ready", "Degraded" };
#define ENUM(R,N,O,TYPE,NULLABLE) do { int e; if (!enumeration(R, N, sizeof(N)/sizeof((N)[0]), &e, NULLABLE)) return false; O = (TYPE)e; } while (0)

static unsigned decimal(const char *s, size_t n) { unsigned v = 0; size_t i; for (i = 0; i < n; ++i) { if (!digit((unsigned char)s[i])) return UINT_MAX; v = v * 10u + (unsigned)(s[i] - '0'); } return v; }
static bool date_valid(const char *s) {
    size_t n = strlen(s), p = 19; unsigned y, m, d, hh, mm, ss, days; static const unsigned month_days[] = { 31,28,31,30,31,30,31,31,30,31,30,31 };
    if (n < 20 || s[4] != '-' || s[7] != '-' || s[10] != 'T' || s[13] != ':' || s[16] != ':') return false;
    y = decimal(s,4); m = decimal(s+5,2); d = decimal(s+8,2); hh = decimal(s+11,2); mm = decimal(s+14,2); ss = decimal(s+17,2);
    if (y < 1 || y > 9999 || m < 1 || m > 12 || hh > 23 || mm > 59 || ss > 59) return false;
    days = month_days[m-1] + ((m == 2 && y % 4 == 0 && (y % 100 != 0 || y % 400 == 0)) ? 1u : 0u); if (d < 1 || d > days) return false;
    if (s[p] == '.') { size_t start = ++p; while (p < n && digit((unsigned char)s[p])) ++p; if (p == start || p-start > 7) return false; }
    if (p+1 == n && s[p] == 'Z') return true;
    return p+6 == n && (s[p] == '+' || s[p] == '-') && s[p+3] == ':' && decimal(s+p+1,2) <= 14 && decimal(s+p+4,2) <= 59 && (decimal(s+p+1,2) != 14 || decimal(s+p+4,2) == 0);
}
static bool date(reader *r, char *out, bool *present) { if (!text(r,out,CAVE_FLOW_DATE_BYTES,48,present)) return false; return (present != NULL && !*present) || date_valid(out) || fail(r,CAVE_FLOW_MALFORMED); }

/* Object helpers enforce commas, required fields, and duplicate known keys.
 * Very long unknown keys are validated but skipped instead of clipped/matched. */
static bool object_start(reader *r, bool *empty) { if (!take(r,'{')) return false; space(r); *empty = r->p < r->n && r->s[r->p] == '}'; if (*empty) ++r->p; return true; }
static bool object_key(reader *r, const char *const *keys, size_t count, size_t *index) { char key[64]; bool overflow = false; size_t i; if (!string_value(r,key,sizeof(key),0,&overflow) || !take(r,':')) return false; *index = count; if (!overflow) for (i=0;i<count;++i) if (strcmp(keys[i],key)==0) { *index=i; break; } return true; }
static bool seen_key(reader *r, uint32_t *seen, size_t index, size_t count) { if (index < count) { uint32_t bit = UINT32_C(1) << index; if ((*seen & bit) != 0) return fail(r,CAVE_FLOW_MALFORMED); *seen |= bit; } return true; }
static bool object_next(reader *r, bool *done) { space(r); *done = r->p < r->n && r->s[r->p] == '}'; if (*done) { ++r->p; return true; } return take(r,','); }
static bool required(reader *r, uint32_t seen, size_t count) { return seen == ((UINT32_C(1) << count)-1u) || fail(r,CAVE_FLOW_MALFORMED); }

static bool agent(reader *r, cave_flow_agent *a) {
    static const char *const keys[] = { "agentId","displayName","agentType","isSubagent","state","phase","summary","currentFocus","focusEvidence","startedAtUtc","updatedAtUtc","evidence","summaryEvidence" };
    uint32_t seen = 0; bool done; size_t k, i;
    if (!object_start(r,&done)) return false;
    while (!done) {
        if (!object_key(r,keys,13,&k) || !seen_key(r,&seen,k,13)) return false;
        switch (k) {
        case 0: if (!text(r,a->agent_id,sizeof(a->agent_id),64,NULL)) return false; if (strlen(a->agent_id) != 64) return fail(r,CAVE_FLOW_MALFORMED); for (i=0;i<64;++i) if (!((a->agent_id[i]>='0' && a->agent_id[i]<='9') || (a->agent_id[i]>='a' && a->agent_id[i]<='f'))) return fail(r,CAVE_FLOW_MALFORMED); break;
        case 1: if (!text(r,a->display_name,sizeof(a->display_name),32,NULL)) return false; break;
        case 2: if (!text(r,a->agent_type,sizeof(a->agent_type),64,NULL)) return false; break;
        case 3: if (!boolean(r,&a->is_subagent)) return false; break;
        case 4: ENUM(r,states,a->state,cave_flow_state,false); break;
        case 5: ENUM(r,phases,a->phase,cave_flow_phase,true); break;
        case 6: if (!text(r,a->summary,sizeof(a->summary),160,&a->has_summary)) return false; break;
        case 7: if (!text(r,a->current_focus,sizeof(a->current_focus),160,&a->has_current_focus)) return false; break;
        case 8: ENUM(r,evidence,a->focus_evidence,cave_flow_evidence,true); if (a->focus_evidence == CAVE_FLOW_OBSERVED) return fail(r,CAVE_FLOW_MALFORMED); break;
        case 9: if (!date(r,a->started_at_utc,NULL)) return false; break;
        case 10: if (!date(r,a->updated_at_utc,NULL)) return false; break;
        case 11: ENUM(r,evidence,a->evidence,cave_flow_evidence,true); break;
        case 12: ENUM(r,evidence,a->summary_evidence,cave_flow_evidence,true); break;
        default: if (!skip(r)) return false; break;
        }
        if (!object_next(r,&done)) return false;
    }
    return required(r,seen,13) && ((a->has_current_focus == (a->has_summary && a->summary_evidence == CAVE_FLOW_DECLARED)
        && (a->has_current_focus
            ? a->focus_evidence == CAVE_FLOW_DECLARED && strcmp(a->current_focus,a->summary) == 0
            : a->focus_evidence == CAVE_FLOW_NO_EVIDENCE)) || fail(r,CAVE_FLOW_MALFORMED));
}
static bool goal(reader *r, cave_flow_goal *g) {
    static const char *const keys[] = { "objective","isTruncated","status","tokenBudget","tokensUsed","timeUsedSeconds","createdAt","updatedAt" };
    uint32_t seen = 0; bool done; size_t k;
    if (!object_start(r,&done)) return false;
    while (!done) {
        if (!object_key(r,keys,8,&k) || !seen_key(r,&seen,k,8)) return false;
        switch (k) {
        case 0: if (!text(r,g->objective,sizeof(g->objective),2048,NULL)) return false; break;
        case 1: if (!boolean(r,&g->is_truncated)) return false; break;
        case 2: ENUM(r,goal_states,g->status,cave_flow_goal_state,false); break;
        case 3: if (!integer64(r,&g->token_budget,&g->has_token_budget)) return false; break;
        case 4: if (!integer64(r,&g->tokens_used,NULL)) return false; break;
        case 5: if (!integer64(r,&g->time_used_seconds,NULL)) return false; break;
        case 6: if (!integer64(r,&g->created_at,NULL)) return false; break;
        case 7: if (!integer64(r,&g->updated_at,NULL)) return false; break;
        default: if (!skip(r)) return false; break;
        }
        if (!object_next(r,&done)) return false;
    }
    return required(r,seen,8) && (((!g->has_token_budget || g->token_budget > 0)
        && g->tokens_used >= 0 && g->time_used_seconds >= 0) || fail(r,CAVE_FLOW_MALFORMED));
}
static bool main_goal(reader *r, cave_flow_main_goal *g) {
    static const char *const keys[] = { "status","goal","retrievedAtUtc","error" };
    uint32_t seen = 0; bool done; size_t k;
    if (!object_start(r,&done)) return false;
    while (!done) {
        if (!object_key(r,keys,4,&k) || !seen_key(r,&seen,k,4)) return false;
        switch (k) {
        case 0: ENUM(r,goal_sources,g->status,cave_flow_goal_source,false); break;
        case 1: g->has_goal = !is_null(r); if (g->has_goal ? !goal(r,&g->goal) : !literal(r,"null")) return false; break;
        case 2: if (!date(r,g->retrieved_at_utc,&g->has_retrieved_at)) return false; break;
        case 3: if (!text(r,g->error,sizeof(g->error),512,&g->has_error)) return false; break;
        default: if (!skip(r)) return false; break;
        }
        if (!object_next(r,&done)) return false;
    }
    return required(r,seen,4) && (!g->has_goal || g->status == CAVE_FLOW_GOAL_READY || fail(r,CAVE_FLOW_MALFORMED));
}
static bool agents(reader *r, cave_flow_snapshot *s) {
    if (!take(r,'[')) return false;
    space(r);
    if (r->p < r->n && r->s[r->p] == ']') { ++r->p; return true; }
    for (;;) { if (s->agent_count >= CAVE_FLOW_MAX_AGENTS) return fail(r,CAVE_FLOW_TOO_LARGE); if (!agent(r,&s->agents[s->agent_count])) return false; ++s->agent_count; space(r); if (r->p < r->n && r->s[r->p] == ']') { ++r->p; return true; } if (!take(r,',')) return false; }
}
static bool snapshot(reader *r, cave_flow_snapshot *s) {
    static const char *const keys[] = { "schemaVersion","workspaceId","workspaceName","sourceMode","generatedAtUtc","sourceUpdatedAtUtc","sourceStatus","error","totalAgentCount","agents","mainGoal" };
    uint32_t seen = 0; bool done; size_t k;
    if (!object_start(r,&done)) return false;
    while (!done) {
        if (!object_key(r,keys,11,&k) || !seen_key(r,&seen,k,11)) return false;
        switch (k) {
        case 0: if (!unsigned32(r,&s->schema_version)) return false; if (s->schema_version != 1) return fail(r,CAVE_FLOW_UNSUPPORTED); break;
        case 1: if (!text(r,s->workspace_id,sizeof(s->workspace_id),128,NULL)) return false; if (s->workspace_id[0] == '\0') return fail(r,CAVE_FLOW_MALFORMED); break;
        case 2: if (!text(r,s->workspace_name,sizeof(s->workspace_name),160,NULL)) return false; break;
        case 3: ENUM(r,modes,s->source_mode,cave_flow_mode,false); break;
        case 4: if (!date(r,s->generated_at_utc,NULL)) return false; break;
        case 5: if (!date(r,s->source_updated_at_utc,&s->has_source_updated_at)) return false; break;
        case 6: ENUM(r,sources,s->source_status,cave_flow_source,false); break;
        case 7: if (!text(r,s->error,sizeof(s->error),512,&s->has_error)) return false; break;
        case 8: if (!unsigned32(r,&s->total_agent_count)) return false; break;
        case 9: if (!agents(r,s)) return false; break;
        case 10: if (!main_goal(r,&s->main_goal)) return false; break;
        default: if (!skip(r)) return false; break;
        }
        if (!object_next(r,&done)) return false;
    }
    return required(r,seen,11) && (s->total_agent_count >= s->agent_count || fail(r,CAVE_FLOW_MALFORMED));
}
static cave_flow_result parse_response(const char *json, size_t length, cave_flow_snapshot *scratch,
    cave_flow_snapshot *published, const char *expected_workspace) {
    reader r;
    if (json == NULL || scratch == NULL || published == NULL || scratch == published) return CAVE_FLOW_BAD_ARGUMENT;
    if (length > CAVE_FLOW_MAX_RESPONSE_BYTES) return CAVE_FLOW_TOO_LARGE;
    r.s=(const unsigned char *)json; r.n=length; r.p=0; r.depth=0; r.error=CAVE_FLOW_MALFORMED;
    memset(scratch,0,sizeof(*scratch));
    if (!snapshot(&r,scratch)) return r.error;
    space(&r); if (r.p != r.n) return CAVE_FLOW_MALFORMED;
    if (expected_workspace != NULL && strcmp(scratch->workspace_id,expected_workspace) != 0) return CAVE_FLOW_WORKSPACE_MISMATCH;
    *published=*scratch;
    return CAVE_FLOW_OK;
}
cave_flow_result cave_flow_parse(const char *json, size_t length, cave_flow_snapshot *scratch, cave_flow_snapshot *published) {
    return parse_response(json,length,scratch,published,NULL);
}
cave_flow_result cave_flow_poll(cave_flow_http_get http_get, void *context, const char *workspace_id,
    char *body, size_t capacity, cave_flow_snapshot *scratch, cave_flow_snapshot *published, const volatile bool *cancelled) {
    static const char prefix[]="/api/agent-flow?workspace="; static const char digits[]="0123456789ABCDEF";
    char path[sizeof(prefix)+CAVE_FLOW_ID_BYTES*3u]; size_t n=sizeof(prefix)-1u, i, length=0; unsigned status=0; cave_flow_result result;
    if (http_get==NULL || workspace_id==NULL || body==NULL || capacity==0 || scratch==NULL || published==NULL || scratch==published) return CAVE_FLOW_BAD_ARGUMENT;
    if (cancelled!=NULL && *cancelled) return CAVE_FLOW_CANCELLED;
    memcpy(path,prefix,n);
    for (i=0;workspace_id[i]!='\0';++i) { unsigned char c=(unsigned char)workspace_id[i]; if (i>=CAVE_FLOW_ID_BYTES-1u) return CAVE_FLOW_TOO_LARGE;
        if ((c>='a' && c<='z') || (c>='A' && c<='Z') || (c>='0' && c<='9') || c=='-' || c=='_' || c=='.' || c=='~') path[n++]=(char)c;
        else { path[n++]='%'; path[n++]=digits[c>>4]; path[n++]=digits[c&15u]; }
    }
    if (i==0) return CAVE_FLOW_BAD_ARGUMENT;
    path[n]='\0';
    result=http_get(context,path,body,capacity,&length,&status,cancelled);
    if (cancelled!=NULL && *cancelled) return CAVE_FLOW_CANCELLED;
    if (result!=CAVE_FLOW_OK) return result;
    if (length>capacity || length>CAVE_FLOW_MAX_RESPONSE_BYTES) return CAVE_FLOW_TOO_LARGE;
    if (status!=200) return CAVE_FLOW_HTTP_ERROR;
    return parse_response(body,length,scratch,published,workspace_id);
}
const char *cave_flow_result_name(cave_flow_result result) {
    switch (result) { case CAVE_FLOW_OK: return "Ready"; case CAVE_FLOW_BAD_ARGUMENT: return "Bad argument"; case CAVE_FLOW_MALFORMED: return "Malformed feed"; case CAVE_FLOW_UNSUPPORTED: return "Unsupported feed"; case CAVE_FLOW_TOO_LARGE: return "Feed too large"; case CAVE_FLOW_TRANSPORT_ERROR: return "Transport unavailable"; case CAVE_FLOW_CANCELLED: return "Cancelled"; case CAVE_FLOW_HTTP_ERROR: return "HTTP unavailable"; case CAVE_FLOW_WORKSPACE_MISMATCH: return "Workspace mismatch"; default: return "Unknown result"; }
}

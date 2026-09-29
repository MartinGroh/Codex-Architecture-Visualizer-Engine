#include "cave_agent_flow.h"
#include "poll_example.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Test-only file I/O uses the CRT; the library/example never use heap APIs. */
static cave_flow_snapshot scratch, published, sentinel;
static cave_flow_example example;
static char input[CAVE_FLOW_MAX_RESPONSE_BYTES+2u], changed[CAVE_FLOW_MAX_RESPONSE_BYTES+2u], body[CAVE_FLOW_MAX_RESPONSE_BYTES];
static unsigned checks;
int cave_flow_cpp_smoke(void);
#define CHECK(C) do { ++checks; if (!(C)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#C); exit(1); } } while (0)
static const char empty_ready[] = "{\"schemaVersion\":1,\"workspaceId\":\"demo\",\"workspaceName\":\"Demo\",\"sourceMode\":\"Demo\",\"generatedAtUtc\":\"2026-09-27T12:00:00Z\",\"sourceUpdatedAtUtc\":null,\"sourceStatus\":\"Unobserved\",\"error\":null,\"totalAgentCount\":0,\"agents\":[],\"mainGoal\":{\"status\":\"Ready\",\"goal\":null,\"retrievedAtUtc\":null,\"error\":null}}";
static cave_flow_result parse(const char *json) { return cave_flow_parse(json,strlen(json),&scratch,&published); }
static void replace(const char *source, const char *from, const char *to) {
    const char *match=strstr(source,from); size_t before, tail; CHECK(match!=NULL); before=(size_t)(match-source); tail=strlen(match+strlen(from));
    CHECK(before+strlen(to)+tail < sizeof(changed)); memcpy(changed,source,before); memcpy(changed+before,to,strlen(to)); memcpy(changed+before+strlen(to),match+strlen(from),tail+1u);
}
static void rejected(const char *json, cave_flow_result expected) { cave_flow_result actual; memset(&published,0x5a,sizeof(published)); sentinel=published; actual=parse(json); if(actual!=expected) fprintf(stderr,"Rejected fixture check %u: expected %s, got %s\n",checks,cave_flow_result_name(expected),cave_flow_result_name(actual)); CHECK(actual==expected); CHECK(memcmp(&published,&sentinel,sizeof(published))==0); }
static size_t load(const char *path) {
    FILE *file=fopen(path,"rb"); size_t n; CHECK(file!=NULL); n=fread(input,1,sizeof(input)-1u,file); CHECK(!ferror(file)); CHECK(feof(file)); fclose(file); input[n]='\0'; return n;
}
/* Fixture mutations use LF regardless of Git's checkout settings. Raw JSON is
 * parsed before normalization, and both wire line endings are checked below. */
static void normalize_fixture_lines(char *text) {
    char *read=text, *write=text;
    while (*read!='\0') {
        if (*read=='\r' && read[1]=='\n') ++read;
        *write++=*read++;
    }
    *write='\0';
}
static void line_ending_tests(void) {
    size_t i, p=0, lines=0, n;
    normalize_fixture_lines(input);
    CHECK(parse(input)==CAVE_FLOW_OK);
    n=strlen(input);
    for(i=0;i<n;++i) if(input[i]=='\n') ++lines;
    CHECK(n+lines<sizeof(changed));
    for(i=0;i<n;++i) {
        if(input[i]=='\n') changed[p++]='\r';
        changed[p++]=input[i];
    }
    changed[p]='\0';
    CHECK(parse(changed)==CAVE_FLOW_OK);
    normalize_fixture_lines(changed);
    CHECK(strcmp(input,changed)==0);
}
static void fixture_tests(const char *path) {
    size_t n=load(path); CHECK(cave_flow_parse(input,n,&scratch,&published)==CAVE_FLOW_OK); CHECK(published.agent_count==3);
    CHECK(published.source_mode==CAVE_FLOW_DEMO); CHECK(published.agents[0].has_current_focus); CHECK(published.agents[0].focus_evidence==CAVE_FLOW_DECLARED);
    CHECK(!published.agents[0].has_parent_agent_id && published.agents[0].has_last_observed_activity);
    CHECK(strcmp(published.agents[0].last_observed_activity,"Validated the JSON contract")==0);
    CHECK(published.agents[1].has_parent_agent_id && strcmp(published.agents[1].parent_agent_id,published.agents[0].agent_id)==0);
    CHECK(strcmp(published.agents[1].display_name,"Chlo\xc3\xab \xf0\x9f\x94\x8e")==0);
    CHECK(strchr(published.agents[1].summary,'\n')!=NULL); CHECK(!published.agents[1].has_current_focus);
    CHECK(published.agents[2].has_summary && published.agents[2].summary[0]=='\0'); CHECK(published.agents[2].phase==CAVE_FLOW_NO_PHASE);
    CHECK(published.agents[2].has_current_focus && published.agents[2].current_focus[0]=='\0');
    CHECK(published.main_goal.has_goal && !published.main_goal.goal.has_token_budget); CHECK(published.main_goal.goal.tokens_used==0);
    CHECK(published.main_goal.goal.created_at==INT64_MIN && published.main_goal.goal.updated_at==INT64_MAX);
    line_ending_tests();
    replace(input,"\"tokenBudget\": null","\"tokenBudget\": 1"); CHECK(parse(changed)==CAVE_FLOW_OK); CHECK(published.main_goal.goal.has_token_budget && published.main_goal.goal.token_budget==1);
    replace(input,"\"tokenBudget\": null","\"tokenBudget\": 0"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"tokenBudget\": null","\"tokenBudget\": -1"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"tokensUsed\": 0","\"tokensUsed\": -1"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"timeUsedSeconds\": 0","\"timeUsedSeconds\": -1"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"9223372036854775807","9223372036854775808"); rejected(changed,CAVE_FLOW_TOO_LARGE);
    replace(input,"\"phase\": \"Reading\"","\"phase\": \"FuturePhase\""); rejected(changed,CAVE_FLOW_UNSUPPORTED);
    replace(input,"\"focusEvidence\": \"Declared\"","\"focusEvidence\": null"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"currentFocus\": \"Validate JSON contract\"","\"currentFocus\": null"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"currentFocus\": \"Validate JSON contract\"","\"currentFocus\": \"\""); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"currentFocus\": \"Validate JSON contract\"","\"currentFocus\": \"Different declaration\""); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"focusEvidence\": null","\"focusEvidence\": \"Declared\""); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"status\": \"Ready\",\n    \"goal\"","\"status\": \"Private\",\n    \"goal\""); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\\ud83d\\udd0e","\\ud83dX"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\\u00eb","\\u0000"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\\u00eb","\\udc00"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"2026-09-27T12:00:00.0000000+00:00","2026-02-30T12:00:00Z"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"agentId\": \"a","\"agentId\": \"A"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(input,"\"parentAgentId\": \"a","\"parentAgentId\": \"A"); rejected(changed,CAVE_FLOW_MALFORMED);
}
static void empty_and_unknown_tests(void) {
    CHECK(parse(empty_ready)==CAVE_FLOW_OK); CHECK(published.agent_count==0 && published.total_agent_count==0); CHECK(!published.main_goal.has_goal); CHECK(published.main_goal.status==CAVE_FLOW_GOAL_READY); CHECK(!published.has_source_updated_at);
    replace(empty_ready,"\"status\":\"Ready\"","\"status\":\"Private\""); CHECK(parse(changed)==CAVE_FLOW_OK); CHECK(published.main_goal.status==CAVE_FLOW_GOAL_PRIVATE && !published.main_goal.has_goal);
    replace(empty_ready,"\"status\":\"Ready\"","\"status\":\"Unbound\""); CHECK(parse(changed)==CAVE_FLOW_OK); CHECK(published.main_goal.status==CAVE_FLOW_GOAL_UNBOUND);
    replace(empty_ready,"\"status\":\"Ready\"","\"status\":\"Unavailable\""); CHECK(parse(changed)==CAVE_FLOW_OK); CHECK(published.main_goal.status==CAVE_FLOW_GOAL_UNAVAILABLE);
    replace(empty_ready,"\"schemaVersion\":1","\"schemaVersion\":2"); rejected(changed,CAVE_FLOW_UNSUPPORTED);
    replace(empty_ready,"\"schemaVersion\":1","\"schemaVersion\":1,\"schemaVersion\":1"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"schemaVersion\":1","\"schemaVersion\":01"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"schemaVersion\":1","\"schemaVersion\":1e0"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"workspaceName\":\"Demo\",",""); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"error\":null","\"error\":false"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"error\":null","\"error\":\"\""); CHECK(parse(changed)==CAVE_FLOW_OK); CHECK(published.has_error && published.error[0]=='\0');
    replace(empty_ready,"\"schemaVersion\":1","\"future\":{\"nested\":[true,false,null,-1.2e+9,{\"text\":\"\\u0000\"}]},\"schemaVersion\":1"); CHECK(parse(changed)==CAVE_FLOW_OK);
    replace(empty_ready,"\"schemaVersion\":1","\"future\":[true,],\"schemaVersion\":1"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"schemaVersion\":1","\"future\":{\"a\":1 \"b\":2},\"schemaVersion\":1"); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"workspaceName\":\"Demo\"","\"workspaceName\":\"\xc0\xaf\""); rejected(changed,CAVE_FLOW_MALFORMED);
    replace(empty_ready,"\"workspaceName\":\"Demo\"","\"workspaceName\":\"\xed\xa0\x80\""); rejected(changed,CAVE_FLOW_MALFORMED);
    snprintf(changed,sizeof(changed),"%s trailing",empty_ready); rejected(changed,CAVE_FLOW_MALFORMED);
    CHECK(cave_flow_parse(empty_ready,strlen(empty_ready)-1u,&scratch,&published)==CAVE_FLOW_MALFORMED);
    CHECK(cave_flow_parse(empty_ready,CAVE_FLOW_MAX_RESPONSE_BYTES+1u,&scratch,&published)==CAVE_FLOW_TOO_LARGE);
    CHECK(cave_flow_parse(NULL,0,&scratch,&published)==CAVE_FLOW_BAD_ARGUMENT);
    CHECK(cave_flow_parse(empty_ready,strlen(empty_ready),&published,&published)==CAVE_FLOW_BAD_ARGUMENT);
}
static void bounds_tests(void) {
    char long_text[164], nested[140]; size_t i;
    memset(long_text,'x',161); long_text[161]='\0'; snprintf(nested,sizeof(nested),"\"workspaceName\":\"%s\"", "");
    /* Build directly: the permitted 160-unit text followed by one extra unit. */
    strcpy(input,"\"workspaceName\":\""); strcat(input,long_text); strcat(input,"\""); replace(empty_ready,"\"workspaceName\":\"Demo\"",input); rejected(changed,CAVE_FLOW_TOO_LARGE);
    for(i=0;i<33;++i) nested[i]='[';
    nested[33]='0';
    for(i=34;i<67;++i) nested[i]=']';
    nested[67]='\0';
    snprintf(input,sizeof(input),"\"future\":%s,\"schemaVersion\":1",nested); replace(empty_ready,"\"schemaVersion\":1",input); rejected(changed,CAVE_FLOW_TOO_LARGE);
    /* Repeating a full valid row checks the array bound, not only body length. */
    load("tests/fixture.json");
    normalize_fixture_lines(input);
    { const char *start=strstr(input,"    {\n      \"agentId\""); const char *end; char row[2048]; size_t row_length, count, p=0;
      CHECK(start!=NULL); end=strstr(start,"    }"); CHECK(end!=NULL); row_length=(size_t)(end+5-start);
      CHECK(row_length<sizeof(row)); memcpy(row,start,row_length); row[row_length]='\0';
      changed[p++]='['; for(count=0;count<33;++count) { if(count!=0) changed[p++]=','; memcpy(changed+p,row,row_length); p+=row_length; } changed[p++]=']'; changed[p]='\0';
      memcpy(input,changed,p+1u); replace(empty_ready,"[]",input); rejected(changed,CAVE_FLOW_TOO_LARGE);
    }
}
typedef struct { unsigned calls, renders, statuses; cave_flow_result result; unsigned status; size_t reported_size; bool cancel_inside; const char *response; char path[1200]; } fake_transport;
static cave_flow_result get(void *context,const char *path,char *target,size_t cap,size_t *n,unsigned *status,const volatile bool *cancelled) {
    fake_transport *f=(fake_transport *)context; const char *response=f->response!=NULL?f->response:empty_ready; size_t len=strlen(response); (void)cancelled; ++f->calls; strcpy(f->path,path); CHECK(len<=cap); memcpy(target,response,len); *n=f->reported_size!=0?f->reported_size:len; *status=f->status;
    if(f->cancel_inside) example.cancelled=true;
    return f->result;
}
static void render(void *context,const cave_flow_snapshot *s) { fake_transport *f=(fake_transport *)context; ++f->renders; CHECK(s->schema_version==1); }
static void status(void *context,cave_flow_result result) { fake_transport *f=(fake_transport *)context; ++f->statuses; CHECK(result!=CAVE_FLOW_OK); }
static void transport_tests(void) {
    fake_transport f={0}; volatile bool cancelled=true; f.status=200; f.result=CAVE_FLOW_OK;
    CHECK(cave_flow_poll(get,&f,"a&b /",body,sizeof(body),&scratch,&published,&cancelled)==CAVE_FLOW_CANCELLED); CHECK(f.calls==0);
    replace(empty_ready,"\"workspaceId\":\"demo\"","\"workspaceId\":\"a&b /\""); f.response=changed;
    cancelled=false; CHECK(cave_flow_poll(get,&f,"a&b /",body,sizeof(body),&scratch,&published,&cancelled)==CAVE_FLOW_OK); CHECK(strcmp(f.path,"/api/agent-flow?workspace=a%26b%20%2F")==0);
    f.response=NULL; sentinel=published; CHECK(cave_flow_poll(get,&f,"different-workspace",body,sizeof(body),&scratch,&published,NULL)==CAVE_FLOW_WORKSPACE_MISMATCH); CHECK(memcmp(&sentinel,&published,sizeof(published))==0);
    sentinel=published; f.status=404; CHECK(cave_flow_poll(get,&f,"demo",body,sizeof(body),&scratch,&published,NULL)==CAVE_FLOW_HTTP_ERROR); CHECK(memcmp(&sentinel,&published,sizeof(published))==0);
    f.status=200; f.reported_size=sizeof(body)+1u; CHECK(cave_flow_poll(get,&f,"demo",body,sizeof(body),&scratch,&published,NULL)==CAVE_FLOW_TOO_LARGE);
    f.reported_size=0; f.result=CAVE_FLOW_TRANSPORT_ERROR; CHECK(cave_flow_poll(get,&f,"demo",body,sizeof(body),&scratch,&published,NULL)==CAVE_FLOW_TRANSPORT_ERROR);
    f.result=CAVE_FLOW_CANCELLED; CHECK(cave_flow_poll(get,&f,"demo",body,sizeof(body),&scratch,&published,NULL)==CAVE_FLOW_CANCELLED);
    f.result=CAVE_FLOW_OK; CHECK(cave_flow_example_init(&example,get,&f,"demo",render,status,&f,1000)==CAVE_FLOW_OK);
    CHECK(cave_flow_example_tick(&example,UINT32_MAX-500u)); CHECK(!cave_flow_example_tick(&example,100)); CHECK(cave_flow_example_tick(&example,500)); CHECK(f.renders==2);
    f.cancel_inside=true; CHECK(cave_flow_example_tick(&example,1500)); CHECK(f.statuses==1 && example.cancelled); CHECK(!cave_flow_example_tick(&example,2500));
}
int main(int argc,char **argv) {
    CHECK(cave_flow_cpp_smoke()==1);
    fixture_tests(argc>1?argv[1]:"tests/fixture.json"); empty_and_unknown_tests(); bounds_tests(); transport_tests();
    printf("PASS %u checks; snapshot=%zu bytes; example=%zu bytes; response=%u bytes\n",checks,sizeof(cave_flow_snapshot),sizeof(cave_flow_example),(unsigned)CAVE_FLOW_MAX_RESPONSE_BYTES);
    if(argc>2) { size_t n=load(argv[2]); CHECK(cave_flow_parse(input,n,&scratch,&published)==CAVE_FLOW_OK); printf("PASS pack-root example contract\n"); }
    return 0;
}

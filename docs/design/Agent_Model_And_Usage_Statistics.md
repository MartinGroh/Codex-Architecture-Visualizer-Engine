# Agent models and usage statistics

Implementation plan agreed in scope by the operator on 2026-10-15. Make this plan before product changes and proceed without a plan-approval stop. Preserve the existing UI, activity lifecycle, graph evidence, conversation privacy, and unrelated local repair.

## Product outcome

- Every actual agent icon identifies its model family: Luna/moon, Terra/Earth, Sol/sun, Astra/galaxy. The existing main/subagent silhouette and phase remain intact. A separate compact indicator shows configured reasoning effort; exact model version and effort remain readable and accessible. Unknown models/settings stay explicitly unknown.
- Task statistics describe one main task and its observed descendants: agent counts, model/effort mix, recorded agent time, and available input/cached/output/total tokens. Model family and exact version remain separate grouping choices.
- Project statistics retain the project's observed history across viewer restarts. Overall statistics combine tracked projects and rank comparable project usage, with task and agent drilldown. Parallel agent time is additive agent time, not project wall time or a quality/productivity score.

## Evidence established before implementation

- The installed Codex App Server generated protocol exposes `thread/read` with `includeTurns: false`, exact `id`, `parentThreadId`, current-or-latest-persisted `model` and `reasoningEffort`. These are configured settings, explicitly not per-turn execution telemetry.
- `thread/turns/list` with `itemsView: notLoaded` supplies turn identity, lifecycle, start/end and nullable duration without messages, tool content or hidden reasoning.
- `account/usage/read` accepts an exact `threadId`; optional `threadUsage.groups` reports model/effort and nullable input, cached-input, output and total tokens. Its billing-route estimates are source estimates, not CAVE measurements.
- A live read-only probe of this exact task returned its model and effort, plus historical turn durations. `threadUsage` was null. Supporting the schema does not establish availability for every account, route or task.
- Existing activity retention is bounded and cannot serve as a lifetime statistics database. Account quota percentages and account-wide tokens cannot be apportioned to projects or agents.

## Ownership and data path

1. Keep Codex protocol translation in Infrastructure behind an Application-owned telemetry read port. Reuse the canonical windowless App Server process launcher. One bounded metadata batch performs read-only requests; never start/resume/fork/steer tasks, read transcript files, or enumerate unrelated projects to collect statistics.
2. Application owns exact identity correlation, eligibility, aggregation, percentages and coverage. Extend existing activity presentation enrichment once, then use it from architecture snapshots, workspace overview and activity/Light feeds. Do not introduce a second activity reducer.
3. The per-user HTTP host owns a bounded background collector. It samples hook-observed tasks and descendants; optional backfill discovers native history only for registered project roots. Cache reads for live agent badges never block graph refresh on an external process.
4. Persist one canonical versioned machine-local telemetry record per exact native thread under the existing CAVE application-data root, using atomic replacement and cross-process locking. Store only bounded identities, project membership, model/effort settings, usage groups, durations, timestamps and coverage. No prompt, answer, tool data or hidden reasoning enters this store.
5. Persist cumulative native usage by replacement, not addition on every poll. Deduplicate turns and agent identities. Repeated polling, restart, cache expiry, page overlap and concurrent readers must not increase totals.
6. Keep stable opaque public task/agent keys at HTTP and portable-feed boundaries. Native IDs remain private collection keys. Missing or foreign identity, incompatible data and source failures remain explicit; they never borrow a parent's model or another task's usage.

## Accounting rules

- Keep native usage groups under the source-reported model/version and effort. Never assign a thread's entire history to its latest configured model.
- Preserve null versus genuine zero independently for each token counter. A partial total is labeled partial with known/missing-agent coverage. Percentages use the displayed, stated denominator and cannot imply complete lifetime coverage.
- Attribute prospective observed active intervals only between consecutive bounded samples with unchanged known configuration and continuous observed activity. Gaps, restarts, setting transitions and unknown configuration remain unattributed. Label these as observed agent time, distinct from native completed-turn duration; do not add both representations together.
- Retain native turn duration as task/agent elapsed time independently of model attribution. Historical duration without historical settings remains unattributed by model/effort.
- Global totals deduplicate native threads across workspace memberships. A task observed in multiple projects is shown as shared; its usage is not silently charged in full to each project's ranking. Project-attributed and shared/unassigned usage remain separate.
- Persist tracking start and last successful source observation. Lifetime means retained observed/backfilled history with visible coverage, never invented history before tracking.

## UI implementation

Extend the established Operate surfaces and typography/colors; this is not a redesign or a new visual identity.

- One shared model-family glyph and effort presenter enriches `AgentAvatar` and accompanying accessible labels. Preserve phase motion, reduced-motion behavior and main/subagent distinction. Use authored/library SVG, not emoji. Model badges do not encode a subjective smart/dumb ranking.
- Use a compact effort meter or short effort label, with the exact configured level exposed in details. Never confuse the configured level with the current Thinking phase.
- One shared statistics component supports task/project/overall scope. Place the overall summary and a ranked project comparison on the existing machine main page, project history in the existing project work context, and task details with the existing agent flow/current task. Do not replace the graph or existing dashboard navigation.
- Offer Tokens / Agent time / Agents as explicit comparison metrics, and model-family versus exact-version grouping. Keep token input/output/cache distinctions and the effort breakdown available without dense icon-only controls.
- Empty, first collection, loading, unavailable, partial, zero, stale and shared attribution are explicit states. Small screens keep the main totals and readable rows; detailed comparison scrolls within its own region without widening the page. All icon meanings are available by text and keyboard-accessible details.

## Implementation checkpoints

1. Add typed telemetry/configuration contracts, bounded App Server read adapter and process-contract tests. Verify the current live task read without publishing private text.
2. Add deterministic accounting, atomic persistence and the host-owned collector. Test replacement/deduplication, parallel agents, model switches, gaps, null/zero, foreign IDs, shared membership and restart retention.
3. Enrich canonical activity projections and all agent display consumers, including additive optional fields in portable feeds/schema. Preserve external clients and document their metadata semantics.
4. Add the four family badges, effort presenter and scoped statistics components using the same records and aggregation rules. Update explicit synthetic demo fixtures for all four families, mixed efforts and missing tokens.
5. Run focused tests, full backend/frontend suites, security audits, hook/portable-client checks, metadata validation, CodeGraph status and built host health/API checks. Inspect real desktop/mobile renders in one bounded pass, batch material fixes, then confirm once. Use the Impeccable detector and independent finish review as directed by the UI skill.
6. Deliver a focused reviewable branch/PR with actual validation and source limitations. Use the canonical installer for an authorized local install and verify installed hashes, listener ownership and functional health; never hand-edit deployed plugin state.

## Completion criteria

The four model families and exact reasoning settings appear consistently on agent views; task, project and overall statistics agree; token availability and observed-time coverage are honest; refresh/restart never multiply usage; existing working behavior and unrelated local changes are preserved. Backend, frontend, transport and visible-state verification pass. Update Architecture.md and Codex_Compatibility.md alongside the implementation so the new owner and evidence boundaries remain discoverable.

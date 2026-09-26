# CAVE Chat Interaction — Accepted Implementation

Status: implemented for embedded Codex and experimental trusted-network browser control on 2026-08-23; internet-facing hardening is deliberately deferred.

## Goal

Let a CAVE viewer follow the public conversation associated with a workspace, including the main Codex agent and sub-agents. In the browser view, an animated robot should show a short activity bubble. Selecting it should open a Codex-like conversation drawer and, when a secure control channel is available, allow the operator to send a message back to the same Codex task.

The activity bubble must show observable status or public commentary, not hidden chain-of-thought.

## Confirmed Codex capabilities

Codex hooks provide lifecycle-boundary conversation data:

- `UserPromptSubmit` includes the submitted user prompt.
- `Stop` includes the main agent's final assistant message.
- `SubagentStop` includes the sub-agent identity and final assistant message.
- Hooks do not provide live assistant-message deltas.
- Hook transcript files are not a stable public data contract and must not become CAVE's integration boundary.

See the [Codex Hooks documentation](https://learn.chatgpt.com/docs/hooks).

The Codex App Server provides deeper thread integration primitives:

- streamed public agent-message deltas;
- thread read/resume operations;
- `turn/start` for a new instruction;
- `turn/steer` for additional input during an active turn.
- `thread/fork` with `ephemeral: true` for in-memory side chats that inherit task context without appearing in stored task listings.

Codex Desktop already runs an App Server over a private connection. The embedded path uses MCP Apps `ui/message`, which the owning host routes to its own chat. For the standalone trusted-network browser, CAVE records the exact hook-observed Codex session/task identity and durably queues a user message. If the task is idle and resumable, the host can validate that `thread/read` and `thread/resume` resolve that exact task and use `turn/start`. If Codex Desktop still owns the task writer, the synchronous plugin `Stop` hook claims only messages bound to its own session and returns the documented continuation decision. Codex then creates the next user prompt inside the existing owner instead of CAVE starting a competing writer. CAVE never silently creates or switches to a different task.

A browser turn is not allowed to compete with the Desktop-owned turn. The five-minute visual activity lease is intentionally not used as an ownership timeout. When `thread/resume` reports `already has an active writer`, CAVE leaves the envelope durable and queued; the owning task's next `Stop` hook converts it into a continuation prompt. The hook claims only queued records for its exact session, keeps other task records untouched, and records the public browser text once without retaining the generated continuation envelope. This is required because a separately launched App Server cannot steer the Desktop process's in-memory active turn. Graceful host shutdown returns an interrupted App Server delivery to the durable queue. A hard-crash record already marked `running` is failed visibly and never replayed because Codex may already have accepted it. App Server approval and user-input requests are currently declined/empty rather than being exposed remotely; a prompt that requires such interaction therefore fails safely and reports delivery failure.

See the [Codex App Server documentation](https://learn.chatgpt.com/docs/app-server).

## Privacy boundary

CAVE's activity journal remains privacy-minimized and deliberately excludes raw prompts, assistant messages, commands, tool payloads, tool responses, and transcript contents. The approved conversation mirror changes persistence only through a distinct, visibly opt-in journal.

The conversation feature must never expose or persist:

- hidden chain-of-thought or raw reasoning text;
- system or developer instructions;
- tool inputs, tool outputs, commands, or approval payloads;
- transcript-file contents as an undocumented shortcut;
- credentials, tokens, or known secret values.

Public reasoning summaries may only be considered as an explicit opt-in after their behavior is verified. The default robot bubble should use CAVE's existing safe phases and summaries, such as reading, editing, validating, or waiting.

## Security boundary

The HTTP/SSE viewer on `0.0.0.0:5098` now includes conversation-sharing and exact-task message-posting endpoints. This is an explicit user-approved development-stage operating mode for loopback and the operator's trusted Tailscale/private network only. It is unauthenticated and must not be exposed directly to the public internet. Authentication, authorization, TLS, remote approval handling, audit export, and revocable credentials remain a later hardening slice; their absence must be visible in documentation and must not be described as secure remote access.

Conversation mirroring must be disabled by default and enabled per workspace. The UI must make the sharing state obvious.

## Implemented boundary

### Safe activity presence

- Render a larger animated robot for each active main agent or sub-agent.
- Show a short bubble derived from the existing privacy-safe activity summary.
- Keep main-agent and sub-agent colors/animations visually distinct.
- Clicking the robot opens an Agent Activity drawer with the safe observed activity timeline.
- Do not change message persistence or add mutation endpoints.

### Opt-in conversation mirror

- Introduce a separate, versioned conversation journal owned by the workspace activity infrastructure; do not overload the architecture graph.
- Capture user messages from `UserPromptSubmit` and completed public responses from `Stop` and `SubagentStop`.
- Preserve main-agent versus sub-agent identity, task/session ID, turn ID, timestamp, role, and completion state.
- Stream journal updates to the browser through a read-only endpoint.
- Render a chat drawer containing public user messages and completed main/sub-agent output.
- Bound retention to the newest 512 public messages and truncate oversized individual messages.
- Keep disabling sharing fail-closed and immediately hide retained content. Explicit clearing and age-based retention remain future work.

### Embedded host interaction

- Use current MCP Apps host capabilities for `ui/message` and `sampling/createMessage`.
- Send user-authored composer messages to the owning chat without discovering or resuming a thread.
- Run node questions as isolated sampling requests and keep memo results in client presentation state.
- Read account usage through an application-owned App Server port without thread operations.

### Trusted-network browser interaction

- Resolve only opaque workspace ids from the machine catalog; never accept a browser-supplied filesystem path.
- Require an opt-in conversation-sharing setting and the browser's expected exact session id.
- Persist queued delivery before accepting the HTTP request and expose `queued`, `running`, `completed`, or `failed` state in the canonical snapshot/SSE stream.
- Prefer the synchronous owner-side `Stop` hook when the Desktop-owned task is active; its continuation decision is the canonical handoff for queued messages on that task.
- Keep an owner-hook delivery `running` after the claiming `Stop`; complete it from the first retained main-agent `Final` for the exact session whose timestamp is strictly later than the claim. Codex may reuse the claim turn id for the generated continuation, so turn inequality is not a valid completion signal.
- Keep a message queued when `thread/resume` still reports an active writer; the owner hook may claim it because this failure precedes `turn/start`.
- Revalidate both App Server thread id and session id before starting the turn.
- Recover a gracefully interrupted envelope as queued; fail an ambiguous foreign `running` envelope rather than risk duplicate execution.
- Stream public commentary and the final answer into the same bounded conversation journal used by hooks.
- Decline App Server approval requests until an explicit remote approval contract exists.
- Keep node Explain/Ask separate from this durable shared-chat path: validate the same hook-bound task, create an ephemeral fork, mark its exact session in `.cave/conversation/ephemeral`, run one node-evidence prompt there, and return only the final text to the owning modal. Hooks and projections must exclude marked sessions so side-chat prompts, activity, and answers never enter the public conversation journal or replace the workspace's main-task binding.

### Deferred authenticated hardening

- Add an explicit control-plane service separate from the read-only viewer contract.
- Require authentication, authorization, secure transport, audit records, and revocable credentials.
- Consider `turn/steer` only after the owning active App Server can be addressed without creating a competing owner.
- Make delivery state visible: queued, accepted, rejected, cancelled, or failed.
- Never silently create a different Codex task when the requested task cannot be controlled.

## Suggested data model

A conversation record should contain only the minimum public contract:

- schema version;
- event ID;
- workspace ID;
- Codex session/task ID and turn ID;
- agent ID and agent kind (`main` or `sub-agent`);
- role (`user` or `assistant`);
- message kind (`prompt`, `commentary`, or `final`);
- public message text;
- timestamp and delivery/completion state.

Conversation data remains a distinct overlay. It is not semantic architecture truth, Git truth, or agent scope intent.

## UX direction

- Place the animated robot inside the canvas near the node where the agent is active.
- Keep the complete agent name visible at normal overview zoom.
- Use a compact two- or three-line bubble with safe status or the latest public commentary excerpt.
- Open a dedicated conversation drawer; keep it mutually exclusive with the Information drawer and dismiss transient node menus when either opens.
- Allow filtering by all agents, main agent, or a selected sub-agent.
- Visually distinguish live commentary, completed messages, stale/disconnected state, and locally queued write-back.
- In read-only mode, show why the message composer is unavailable instead of hiding it.

## Required verification

- Hook contract tests for main-agent and sub-agent message capture.
- Privacy tests proving excluded fields never enter the journal or API payload.
- Retention, redaction, ordering, deduplication, and clearing tests.
- App Server protocol tests for reconnect and same-task identity.
- Owner-hook tests proving exact-session queue claims, continuation output, and no duplicate conversation retention.
- HTTP contract tests proving workspace selection and exact expected-session identity are preserved at the control boundary.
- Trusted-network deployment checks proving the listener is not represented as internet-safe.
- Browser tests for robot animation, bubble readability, drawer behavior, filtering, and disconnected state.
- An end-to-end run from Codex Desktop and from a remote browser before write-back is considered complete.

## Accepted privacy decision

The user explicitly approved the following privacy and persistence boundary on 2026-08-19:

The recommended initial approval boundary is:

- sharing off by default and enabled per workspace;
- only user-authored messages and public assistant output;
- no raw reasoning, system/developer instructions, transcript ingestion, or tool data;
- bounded workspace-local retention;
- trusted private-network write-back may be enabled during development on the explicitly accepted unauthenticated boundary;
- authentication and broader internet-facing security remain mandatory before any public exposure.

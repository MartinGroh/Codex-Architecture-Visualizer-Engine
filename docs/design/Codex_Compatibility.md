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

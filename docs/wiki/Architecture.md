# Architecture

CAVE has one normalized architecture graph and several independent evidence overlays.

```text
CodeGraph SDK ─┐
Native Git ────┼─> Infrastructure adapters ─> Application snapshot use case
Agent hooks ───┤                                      │
Conversation ──┘                                      v
                                             HTTP/SSE and MCP App
                                                      │
                                                      v
                                             React graph presentation
```

The dependency direction is inward: Domain contains stable concepts, Application owns consumed ports and orchestration, Infrastructure translates external providers, and Host/MCP/UI compose and present the result.

Important constraints:

- Semantic, Git, activity, declared scope, conversation, and usage retain separate provenance.
- Demo mode is explicit and fully synthetic; it is never a recovery path.
- The browser viewer is read-only and does not authenticate clients.
- The embedded MCP App can send a message to its owning Codex task, but that message is not architecture evidence.
- One canonical implementation owns each behavior.

Read the full [accepted architecture](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/Architecture.md), [organization-neutral guidelines](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/ArchitectureAndCodeGuidlines.md), and [design document](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/design/Codex_Architecture_Visualizer_Design.md).

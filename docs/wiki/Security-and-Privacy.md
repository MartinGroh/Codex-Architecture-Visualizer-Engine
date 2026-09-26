# Security and Privacy

CAVE is local-first, not data-free. Architecture, file paths, change metadata, and activity can reveal sensitive system structure even when source code is absent.

## Data paths

| Source | Default | Stored content | Location |
| --- | --- | --- | --- |
| Semantic index | Live mode only | Provider-owned local index | ignored `.codegraph/` |
| Agent activity | Enabled with trusted hooks | phase, bounded paths, agent/session metadata | ignored `.cave/activity/` |
| Conversation | Off | bounded user prompts and final replies only | ignored `.cave/conversation/` |
| Browser conversation control | Explicit request for the hook-bound task | queued prompt, delivery state, and public reply | ignored `.cave/conversation/control/` |
| Semantic workspace | Explicit edits | engineering entities, relationships, content, boards, and revision | ignored `.cave/semantic-workspace.json` |
| Workspace catalog | Local machine | registered root and availability | local application data |
| Demo | Explicit only | synthetic values | ignored `output/demo/` |

Hooks must not persist prompts, assistant responses, commands, tool payloads, tool results, or reasoning. Conversation is a separate visible opt-in path so the two cannot be confused.

## Network model

The standalone HTTP viewer has no authentication. Keep it on loopback or a trusted restricted network. Do not expose its port to an untrusted LAN or the public internet. CAVE does not own firewall, VPN, identity, or production network policy.

Browser access includes writes: callers can change conversation sharing, queue messages for the exact hook-bound Codex task, request isolated node explanations, and change the semantic workspace through its operations and undo/redo endpoints. The host resolves catalog workspace identifiers and validates task identity or semantic-workspace revision as appropriate. Those checks prevent misrouting and conflicting edits; they are not caller authentication. Everyone who can reach the host must be authorized to use these controls and see any shared content. Conversation sharing starts off, but the unauthenticated sharing endpoint can change that setting.

Node questions use temporary task forks and do not enter the main shared conversation. Browser approvals and user-input requests are declined. Authentication and public-network hardening remain deferred in this development-stage mode; publishing the source does not make the running service suitable for public exposure.

## Before sharing

```powershell
pwsh ./scripts/audit-public-readiness.ps1
```

Also inspect generated release archives and the complete Git history. Follow [SECURITY.md](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/SECURITY.md) for private vulnerability reporting and the [public-release checklist](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/security/PUBLIC_RELEASE_CHECKLIST.md) before changing repository visibility.

The reviewed source, history, media, and dependency scope is recorded in [Public readiness](../release/PUBLIC_READINESS.md). Local `.impeccable/` review captures can contain real activity and account usage; they are ignored and must not be included in a public commit or release.

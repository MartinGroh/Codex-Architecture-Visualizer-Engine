# Wanted

This is the canonical shortlist of functionality where design discussion and contributions would be especially valuable. Open an issue before implementation so scope, ownership, privacy, and compatibility can be agreed.

| Area | Wanted outcome | Guardrails | Suggested first slice |
| --- | --- | --- | --- |
| Cross-platform | Run and package CAVE on Linux and macOS | Preserve one canonical provider contract; no platform-specific behavior leaks into Domain | Make host and MCP packaging runtime-parameterized and verify on one Linux runner |
| Provider ecosystem | Add documented semantic-provider adapters | Explicit mode selection, visible provenance, no fallback to sample data | Define adapter conformance tests around `ISemanticIndex` |
| Architecture history | Compare two saved, user-selected architecture snapshots | Local-only storage by default; retention and deletion must be explicit | Design the snapshot ownership and storage format |
| Review mode | Share a privacy-filtered, immutable architecture review bundle | Never include paths or activity unless explicitly selected | Specify an export manifest and redaction preview |
| Impact analysis | Explain likely downstream impact of selected Git changes | Separate exact dependency evidence from inference | Rank existing graph routes without adding generative truth |
| Accessibility | Full keyboard traversal and improved screen-reader summaries | Preserve dense graph usability and semantic structure | Audit cards, routes, panels, and focus order against WCAG |
| Performance | Smooth very large graphs | No hidden truncation; limits must be visible and configurable | Add repeatable 1k/10k synthetic graph benchmarks |
| Testing | End-to-end demo smoke tests and visual regression | Use synthetic demo only; no contributor machine data | Verify mode switches, panels, and edge first paint in CI |
| Plugin delivery | Production HTTPS MCP deployment suitable for directory review | Domain verification, least privilege, privacy/terms, explicit human publish gate | Write and review the hosting threat model |
| Extensibility | Stable plugin points for card decorations and evidence panels | Canonical graph ownership remains in Domain/Application | Draft a read-only extension contract and compatibility policy |

## Good first contributions

- Documentation examples and diagrams.
- Accessibility labels and keyboard-focus tests.
- Additional fully synthetic demo graphs.
- Better error messages with actionable recovery.
- Platform-neutral path handling tests.
- Public-readiness audit rules with low false-positive rates.

Items leave this page when they have an accepted issue or roadmap owner. The issue then becomes the canonical status record.

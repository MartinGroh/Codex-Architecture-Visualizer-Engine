# ADR-003: Stable base graph with independent overlays

- Status: Accepted
- Date: 2026-08-16

## Decision

Keep normalized architecture nodes and relationships stable. Model Git deltas, observed activity, declared intent, impact, focus, and selection as independent overlays composed during projection.

## Consequences

- Structural truth is not mutated by temporary UI state.
- New visual cues normally add an overlay/decorator producer rather than changing every node type.
- The UI must identify the evidence source of each fact and may aggregate overlays to visible ancestors.


# ADR-004: Repository-local Codex plugin source

- Status: Accepted
- Date: 2026-08-16

## Decision

Keep the canonical CAVE plugin at `plugins/cave` and expose it through the repository marketplace at `.agents/plugins/marketplace.json`. Install cached copies through Codex commands; never maintain a separate personal source copy.

## Consequences

- Plugin source, skills, hooks, packaging, and application code evolve together.
- Codex must reinstall the local plugin after material changes and use a new task to load changed skills/tools.
- The plugin manifest advertises only capabilities that are currently runnable.


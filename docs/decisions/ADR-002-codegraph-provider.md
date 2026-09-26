# ADR-002: CodeGraph as the v1 semantic provider

- Status: Accepted
- Date: 2026-08-16

## Decision

Pin CodeGraph v1.5.0 and adapt it behind an `ISemanticIndex` port owned by `Cave.Application`. Do not expose CodeGraph models or query its SQLite schema outside the adapter.

## Context

CodeGraph supplies local C# symbol extraction, relationships, impact traversal, incremental synchronization, and a supported CLI/MCP/TypeScript surface. Its tree-sitter and resolver semantics are useful but are not compiler-authoritative Roslyn semantics.

## Consequences

- CAVE preserves source/confidence metadata and never overstates inferred relationships.
- Provider initialization failure is visible; the normal mode does not fall back to sample or stale data.
- A later Roslyn or SCIP provider can implement the same consumer-owned contract if measured gaps justify it.


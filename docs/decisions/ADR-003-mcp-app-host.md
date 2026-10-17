# ADR-003: Dedicated stdio composition for the Codex MCP App

- Status: Accepted
- Date: 2026-08-16

## Decision

Publish `Cave.Mcp` as a framework-dependent .NET 10 stdio server inside the CAVE plugin. It serves the same self-contained React artifact as `Cave.Host`, calls the same application projection and CodeGraph adapter, and owns only MCP transport/session concerns.

## Context

Codex starts plugin MCP servers and owns their stdio lifecycle, while the browser host owns loopback HTTP, SSE, and static-file middleware. Combining both transports in one running process would force either lifecycle to supervise the other and would complicate plugin shutdown. Separate thin composition roots preserve one canonical projection and UI without introducing a transport wrapper or semantic fallback.

## Consequences

- `cave_show_architecture` returns the initial versioned graph and links the MCP App resource.
- The app-only `cave_poll_architecture` tool long-polls the same live workspace monitor without exposing polling noise to the model.
- The MCP App requests the largest display mode supported by the host; the host retains final placement authority.
- Display placement runs independently of graph acquisition. Startup waits at most 30 seconds including the handshake. A missed initial result allows one reattachment through the existing show tool after two seconds, only for the absolute workspace supplied in a complete MCP tool-input notification. Missing identity never falls back to another workspace or HTTP. Startup failure and unmount clean up the bridge, timers and listeners.
- The plugin installer builds and publishes the stdio server before validation and installation.

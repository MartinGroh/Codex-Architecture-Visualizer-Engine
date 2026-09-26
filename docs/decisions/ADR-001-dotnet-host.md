# ADR-001: ASP.NET Core 10 architecture host

- Status: Accepted
- Date: 2026-08-16

## Decision

Use ASP.NET Core 10 on stable .NET 10 for HTTP, SSE, static UI delivery, and browser composition. Use a second thin .NET stdio composition root for the Codex MCP lifecycle. Both hosts share the same application/infrastructure implementation and the same React/strict-TypeScript UI artifact.

## Context

The product design proposed a Node/TypeScript host. The project owner explicitly selected newest stable C#/.NET for the backend and TypeScript for the frontend. .NET 10 is the current supported LTS release; .NET 11 is preview at this decision date.

## Consequences

- Backend ownership remains in shared .NET application and infrastructure assemblies; only transport composition differs between loopback HTTP and Codex stdio.
- A narrow TypeScript bridge is acceptable only if the CodeGraph provider spike demonstrates that no supported structured process interface is available to .NET.
- The frontend remains independently buildable with Vite and is published into the host's static assets.

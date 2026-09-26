# ADR-005: Token-protected LAN host

## Status

Superseded by ADR-007.

## Context

The standalone CAVE browser must be reachable from other devices on the local network. The product design originally kept the default binding on loopback, while the user explicitly requested that the service listen on `0.0.0.0`. Architecture snapshots expose repository structure, so binding every IPv4 interface without an access boundary is not acceptable.

## Decision

`Cave.Host` binds `http://0.0.0.0:5098` by default. Loopback requests remain token-free. Every non-loopback request requires a cryptographically random per-process token unless the operator supplies `Cave:LanAccessToken` through normal ASP.NET Core configuration.

Browser users may provide the generated token once through `cave_token`; the host stores it in an HTTP-only, same-site session cookie and redirects to a token-free URL. API and SSE clients may use `X-CAVE-LAN-Token` or Bearer authentication. The HTTP composition root remains read-only and exposes no mutation tools.

The verification script continues to override the URL with an ephemeral loopback port so automated checks do not expand the machine's network surface.

## Consequences

- A local browser keeps the existing frictionless experience.
- A LAN viewer must obtain the token from the host operator after each process start.
- Binding alone does not bypass the repository's read-only LAN boundary.
- Firewall policy remains outside this repository and is not changed automatically.

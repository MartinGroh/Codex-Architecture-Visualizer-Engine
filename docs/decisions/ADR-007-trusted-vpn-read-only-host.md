# ADR-007: Trusted-VPN read-only host

## Status

Amended 2026-09-26. Supersedes ADR-005. The trusted-network boundary remains accepted; the original read-only browser scope is superseded by the explicit controls documented in `Architecture.md`.

## Context

The CAVE machine viewer must be reachable from the operator's other computers. The operator explicitly chose to use an existing trusted VPN as the access boundary and requested removal of CAVE's per-process LAN token. Architecture snapshots expose repository structure and privacy-minimized activity metadata, so the absence of application authentication must remain visible in the operating contract.

## Decision

`Cave.Host` binds `http://0.0.0.0:5098` by default and performs no HTTP authentication. Browser access includes architecture and activity reads, opt-in conversation-sharing changes, exact-task chat, isolated node questions, and semantic-workspace operations plus undo/redo. HTTP and task-scoped MCP adapters invoke the application owners of those explicit operations; the HTTP host does not expose arbitrary MCP tools or arbitrary workspace paths.

Task identity and save-time revision checks protect routing and data consistency. They do not authenticate callers. Stale browser drafts still need the proposed client expected-revision check before they can be rejected reliably; see `docs/release/PUBLIC_READINESS.md` for that recorded limitation.

VPN and firewall policy own network reachability. CAVE does not open ports, alter firewall policy, advertise itself publicly, or claim that binding to `0.0.0.0` is an access control.

## Consequences

- A trusted VPN client can open `http://<machine-ip>:5098` without a token or cookie exchange.
- Anyone who can reach port 5098 can view shared content and invoke the exposed controls. Network access must be restricted to people authorized for both reading and writing.
- Operators must not publish the port to an untrusted LAN or the public internet.
- Automated verification binds an isolated loopback port and checks the explicit read/write contracts, task binding, sharing defaults, and synthetic demo behavior.

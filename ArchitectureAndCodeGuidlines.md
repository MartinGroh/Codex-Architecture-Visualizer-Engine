# Architecture and Code Guidelines

This document is the organization-neutral engineering contract for CAVE and a reusable starting point for other repositories. Project-specific facts belong in the closest project architecture or design document, not here.

## Authority and change safety

Read the repository instructions, architecture, and applicable design authority before planning a software change. Preserve working behavior and unusual constraints unless the requested change explicitly replaces them. Inspect the worktree first and never overwrite unrelated changes.

When authorities disagree, use this order:

1. Safety, privacy, and legal requirements.
2. Repository agent and contributor instructions.
3. Accepted architecture and architecture decisions.
4. Product design and feature documentation.
5. Local implementation convention.

Record an intentional exception as a decision. Do not allow an implementation accident to become an undocumented architectural rule.

## Dependency direction and ownership

Dependencies point toward stable concepts:

```text
presentation and composition
           ↓
external adapters and persistence
           ↓
application use cases and consumer-owned ports
           ↓
domain concepts and invariants
```

- Domain code owns stable business concepts and invariants. It has no dependency on UI, transport, storage, process, or vendor SDK code.
- Application code coordinates use cases and owns the interfaces it consumes.
- Infrastructure implements application-owned ports and translates external records into domain concepts.
- Hosts and composition roots choose explicit modes, wire dependencies, and own process lifetime.
- UI code owns presentation state and interaction; it does not become a second source of architecture truth.
- A shared behavior has one canonical owner. Callers depend on that owner rather than copying logic.

Ask for architectural review before introducing a new service boundary, persistence system, network protocol, background process, or dependency direction.

## Explicit modes, failures, and evidence

Every supported mode has one canonical implementation selected explicitly at a composition root. Demo, test, offline, or compatibility behavior must be labeled and must never activate as a silent fallback.

Failures remain visible and actionable:

- Do not catch an error only to substitute plausible-looking data.
- Do not hide unsupported states behind empty results.
- Include which boundary failed and what the operator can do next.
- Keep retries bounded and observable.

Keep evidence sources distinct. Observed data, declared intent, inference, sample data, and user input must retain their provenance and confidence. Derived views may combine evidence, but they must not erase where it came from.

## Implementation rules

- Prefer the smallest change that preserves the existing architecture.
- Extend the canonical owner instead of adding wrappers, shadow implementations, compatibility copies, or monkey patches.
- Use configuration for operator choices, not to conceal two accidental implementations of the same behavior.
- Validate inputs at system boundaries and keep internal types precise.
- Keep public APIs small. Document public C# members with XML summaries and exported TypeScript behavior with useful JSDoc when intent is not obvious.
- Name constraints where they matter. A short `CONSTRAINT:` comment should explain why non-obvious code must remain.
- Keep generated state, caches, captures, credentials, and machine-local catalogs out of version control.
- Never add a credential retrieval path to application code. Use the approved machine-local mechanism for the environment in which the software runs.

## Privacy and security

Collect the minimum data required for the feature. Default optional sharing off. Separate public, local, and sensitive data paths so an operator can reason about them independently.

Do not commit:

- credentials, tokens, private keys, certificates, or connection strings with secrets;
- personal absolute paths, internal hostnames, private endpoints, or organization-only identifiers;
- generated semantic indexes, agent journals, conversation journals, logs, or captures from real workspaces;
- customer, production, or incident data.

Treat a repository history scan as a release requirement. Deleting sensitive text from the current tree does not remove it from earlier commits.

## Verification

Verify in proportion to risk and at the boundary that changed:

1. Focused tests for the behavior.
2. Static analysis, lint, and compilation.
3. Relevant integration or host checks.
4. Packaging and installation checks when delivery changes.
5. Installed version, process state, and functional health after deployment.

A deployment is incomplete until the installed artifact and functional health are verified. Do not invent a new delivery path when the repository already owns build, package, install, update, rollback, or verification scripts.

## Pull-request checklist

- [ ] I read the applicable repository instructions, architecture, and design authority.
- [ ] The change has one clear owner and preserves inward dependency direction.
- [ ] Demo or compatibility behavior is explicit and cannot mask a live failure.
- [ ] Evidence provenance remains visible.
- [ ] No unrelated worktree changes were overwritten.
- [ ] Privacy, credentials, logs, paths, and generated state were checked.
- [ ] Focused and proportional verification passed.
- [ ] Non-obvious constraints and public interfaces are documented.
- [ ] Delivery and rollback documentation changed when the release path changed.

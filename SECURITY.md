# Security Policy

## Supported versions

Until the first public stable release, security fixes target the latest release and current `main` only. After `1.0.0`, this table will list supported release lines explicitly.

## Report a vulnerability

On the public repository, use **Security → Advisories → Report a vulnerability** to submit a confidential report. Maintainers must enable and verify GitHub private vulnerability reporting during the publication transition. While that form is unavailable, use the private contact method on the [maintainer's GitHub profile](https://github.com/MartinGroh). Do not open a public issue for a vulnerability, leaked credential, private path, sensitive capture, or exploitable deployment configuration.

Include:

- affected version or commit;
- the boundary and prerequisite conditions;
- minimal reproduction steps;
- impact and data exposed or modified;
- any safe mitigation you have already tested.

Do not access data that is not yours, disrupt another system, persist access, or publish exploit details before a fix is available. Maintainers will acknowledge a report as soon as practical, keep it private while assessing it, and coordinate remediation and disclosure.

## Security boundaries

- The browser host has no HTTP authentication and is designed for loopback or a trusted restricted network only.
- CAVE must not change firewall, VPN, identity, or production network policy.
- Conversation sharing is off by default.
- Activity hooks are metadata-only and must not persist prompt, response, command, tool payload, or reasoning content.
- Demo mode is fully synthetic and cannot activate as a fallback for live provider failure.
- Credentials do not belong in the repository, configuration examples, logs, issue attachments, or chat transcripts.

Run `pwsh ./scripts/audit-public-readiness.ps1 -IncludeHistory` before sharing a branch. The audit requires GitHub noreply author and committer addresses in published history and suppresses finding values in its output. Audit the actual release artifact separately before distribution.

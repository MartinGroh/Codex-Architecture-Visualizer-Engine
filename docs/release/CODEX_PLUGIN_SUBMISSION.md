# Codex Plugin Submission

Publishing a GitHub release and publishing to the Codex Plugins Directory are separate actions. Directory publication is always a human-controlled maintainer gate.

The authoritative process is the [OpenAI plugin submission documentation](https://developers.openai.com/plugins/deploy/submission).

## Current readiness

CAVE currently packages a local stdio MCP server and local browser viewer. A directory submission that exposes the interactive MCP App requires a production MCP server reachable over public HTTPS; localhost, private-network, and local stdio endpoints are not eligible for that hosted connection. Hosting design, authentication, domain verification, privacy review, abuse controls, operations, and rollback therefore require an accepted architecture decision before submission.

A skills-only listing may be technically different, but removing the MCP App would materially change the product. It must be treated as a separate product decision, not a release shortcut.

## Required submission material

- Verified OpenAI platform identity and Apps Management write access.
- Public production MCP URL when submitting MCP functionality.
- Verified developer or company domain.
- Listing name, icon, description, category, screenshots, website, support URL, privacy policy, and terms URL.
- Accurate tool annotations and least-privilege behavior.
- At least five positive and three negative test prompts with expected behavior.
- Skills scanned for policy and security issues.
- Version-specific release notes.

Repository drafts for privacy and terms are in `docs/legal`. They need a stable public HTTPS location before submission.

## Review gate

Before **Submit for review**:

- [ ] The GitHub release and checksum are final.
- [ ] Full verification, public-readiness, dependency, and security checks pass.
- [ ] The production endpoint matches the reviewed release and health is monitored.
- [ ] Test prompts pass against production.
- [ ] Listing claims match actual behavior and privacy defaults.
- [ ] Support, privacy, and terms URLs are public and stable.
- [ ] A maintainer records the exact version and submission date.

After approval, OpenAI does not automatically make the plugin public. A maintainer reviews the approved snapshot and explicitly chooses whether to publish it. Metadata, MCP, or skill changes require a new scan/review/publish cycle. Record the approval and publication in the release issue.

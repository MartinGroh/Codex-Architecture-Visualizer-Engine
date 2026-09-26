# Release Process

CAVE uses Semantic Versioning. `VERSION` is the release source of truth; .NET, frontend, and plugin package versions are synchronized from it.

## Version policy

- **Patch**: compatible defect, security, documentation, or packaging fix.
- **Minor**: compatible functionality, provider, UI, or public API addition.
- **Major**: incompatible API, data, protocol, plugin, or operating-model change.
- Build metadata such as `+codex.<timestamp>` is an installation cache-buster, not a release version and does not identify different source behavior.

Pre-release versions use standard SemVer identifiers such as `0.4.0-beta.1`. Release tags are annotated and named `v<version>`.

## Prepare

1. Start from current `main` with a clean, reviewed worktree.
2. Move completed changelog entries out of **Unreleased**.
3. Synchronize versions:

   ```powershell
   pwsh ./scripts/set-version.ps1 -Version 0.4.0 -Apply
   ```

4. Run the canonical verification and privacy checks:

   ```powershell
   pwsh ./scripts/verify.ps1
   pwsh ./scripts/audit-public-readiness.ps1
   ```

5. Build the distributable plugin archive and checksum:

   ```powershell
   pwsh ./scripts/package-release.ps1
   ```

6. Install the staged package in a clean test environment and verify CAVE version, MCP startup, browser health, demo mode, and one real indexed workspace.

## Tag and GitHub release

After the release pull request is approved and merged:

```powershell
git tag -a v0.4.0 -m "CAVE 0.4.0"
git push origin v0.4.0
```

The release workflow rebuilds the package and creates a **draft** GitHub release with the ZIP, SHA-256 file, and release manifest. A maintainer compares the workflow artifact checksum with the reviewed local artifact, finalizes release notes, and explicitly publishes the GitHub release.

The workflow does not and must not publish to the Codex Plugins Directory.

## Codex directory release

Follow [CODEX_PLUGIN_SUBMISSION.md](CODEX_PLUGIN_SUBMISSION.md). This is a distinct reviewed release channel. Every changed plugin version is rescanned and reviewed, and a maintainer makes the final publish decision after approval.

## Rollback

- GitHub: mark the affected release as a pre-release or remove its assets, publish a security notice when appropriate, and release a new patch. Do not move or rewrite an already published SemVer tag.
- Local plugin: reinstall a known-good release using the existing installer path; the cache-busted installation address may differ while the base SemVer remains identifiable.
- Directory: use the provider's version-management controls and coordinate with review support. Preserve the incident record and publish a corrected version rather than silently replacing source history.

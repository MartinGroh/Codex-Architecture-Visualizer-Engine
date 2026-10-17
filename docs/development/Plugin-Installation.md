# Plugin installation and validation

`scripts/install-local-plugin.ps1` is the canonical local development installer. It builds the shared UI, publishes the MCP server and viewer, updates the source manifest's installation version, validates the complete package, and uses Codex's supported plugin marketplace/add/list commands. It then verifies the reported version and published executable hashes before refreshing and checking the compatible per-user viewer.

Run a read-only preflight before installation:

```powershell
pwsh ./scripts/install-local-plugin.ps1 -PreflightOnly
```

This checks source metadata, the local marketplace and the selected executable. It performs no build, cachebuster write, plugin installation, process stop or viewer restart. A successful preflight does not establish that installation or runtime verification has completed.

If a marketplace with the same name points to another repository, default preflight and installation refuse before building or changing the source version. Review the exact old and target roots with `pwsh ./scripts/install-local-plugin.ps1 -PreflightOnly -ReplaceMarketplaceSource`. After approving that rebind, run the canonical installer with `-ReplaceMarketplaceSource`. Replacement is limited to a validated local CAVE-only source and refuses installed non-CAVE plugins from that marketplace. It uses supported marketplace remove/add commands, checks their results, and re-lists the exact target root before plugin installation. An `alreadyAdded` response never proves the requested root was selected. Hook runners are backed up before registration/cache changes and restored in `finally`. A failure after removal attempts one bounded restoration of the previous source, retaining the original failure and reporting the resulting registration state.

## Codex backend selection

When `CODEX_CLI_PATH` is present, the installer requires an existing absolute executable and verifies its `codex-cli` version response. Codex Desktop provides this backend; no separate CLI installation is required. A present but invalid, blank, relative or non-Codex executable fails visibly, even if another CLI is available on PATH. Only an absent selection permits `codex` resolution through PATH. Every plugin command uses that same resolved executable and checks its exit code and JSON response. `CODEX_HOME`, when configured, must be absolute and determines the plugin cache root.

The installed Desktop backend `codex-cli 0.163.0-alpha.10.2` exposes `plugin add`, `plugin list`, and `plugin marketplace add/list`; its help exposes no plugin validation or cachebuster command. The former `.system/plugin-creator` scripts are runtime-private files removed by Desktop updates and are no longer a build dependency.

## Repository-owned contract

`scripts/plugin-tooling.psm1` is the shared owner of CAVE package validation, local-marketplace identity, monotonic cachebuster updates and executable selection. `validate-plugin-metadata.ps1` adds repository version alignment and marketplace checks. `validate-plugin.ps1` validates a selected package independently of checkout files; `-RequireBinaries` additionally requires both published entry points. Release staging continues to use `validate-plugin-metadata.ps1 -SkipRepositoryChecks -RequireBinaries`.

Validation fails on malformed or duplicate JSON, incorrect identity/version metadata, missing assets or skills, escaping paths or junction references, an unexpected MCP command, and incomplete or invalid hooks. All nine CAVE lifecycle/tool events must retain their Windows launcher, supported numeric timeout and versioned-runner recovery. CAVE's skill files deliberately use plain `name`/`description` YAML scalars; optional skill interface files use their three quoted strings. Unsupported YAML syntax fails explicitly rather than being approximated. Validation uses PowerShell 7 and performs no dependency installation or download.

CAVE retains its existing `.codex-plugin/plugin.json`, `.mcp.json`, `skills/` and default `hooks/hooks.json` layout. Current [official plugin packaging documentation](https://developers.openai.com/plugins/build/plugins) accepts this compatibility format and documents root-relative package paths and hook trust. This repair does not migrate the package to a different format. The [local marketplace format](https://learn.chatgpt.com/docs/enterprise/plugin-management#supported-formats) resolves `./plugins/cave` from the marketplace repository root.

The `+codex.<14 digits>` installation suffix advances from UTC time or the previous suffix plus one, whichever is newer. Repeated installs and clock skew cannot reuse an older source version. Invalid versions and suffix overflow fail without changing the manifest. The installer preserves active tasks' version-addressed hook runners and uses its existing verified-process restart, stable-viewer hash check, viewer protocol 3 and `/health` requirements. It never substitutes manual deployed-cache edits for Codex installation commands.

## Focused verification

```powershell
pwsh ./scripts/test-plugin-tooling.ps1
pwsh ./scripts/validate-plugin-metadata.ps1
pwsh ./scripts/validate-plugin.ps1 -PluginPath ./plugins/cave
pwsh ./scripts/install-local-plugin.ps1 -PreflightOnly
```

The focused script uses isolated fixtures under ignored `artifacts/test-plugin-tooling`, restores environment selections, and removes only its verified fixture directory. It checks package rejection paths, binary requirements, marketplace containment, version monotonicity/overflow, native-command failures and Desktop/PATH precedence. When Desktop supplies an executable, it also makes a read-only marketplace-list request. It never installs a plugin or controls a viewer process. Full installation remains a separate authorized operation and is complete only after the canonical installer verifies the installed payload and functional health.

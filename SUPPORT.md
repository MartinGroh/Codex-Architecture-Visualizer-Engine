# Support

Use GitHub Issues for reproducible defects and focused feature proposals. Use the relevant issue template and remove private paths, logs, repository names, architecture details, prompts, and credentials before posting.

Before opening an issue:

```powershell
pwsh ./scripts/start-demo.ps1
pwsh ./scripts/audit-public-readiness.ps1
```

If the defect only occurs in live mode, include CAVE version, operating system, .NET version, Node.js version, CodeGraph version, the failing command, and a sanitized error message. Never attach `.cave/`, `.codegraph/`, real-workspace screenshots, hook journals, or conversation journals.

Security concerns follow [SECURITY.md](SECURITY.md). General usage and architecture documentation is indexed in the [wiki source](docs/wiki/Home.md).

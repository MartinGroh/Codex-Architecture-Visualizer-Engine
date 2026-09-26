# Getting Started

## Synthetic demo

The demo is safe to show because semantic graph, Git changes, agent activity, conversation, and usage are all generated. It neither indexes nor reads another repository.

```powershell
git clone https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine.git
Set-Location './Codex-Architecture-Visualizer-Engine'
pwsh ./scripts/start-demo.ps1
```

Open the URL printed by the script, then explore Architecture, Live activity, Changes, semantic zoom, card details, Chat, Info, and the theme selector. Press `Ctrl+C` to stop the host.

## Live workspace

Install PowerShell 7, .NET 10, Node.js, Python 3.11+, Codex, and CodeGraph 1.5.0. From the CAVE repository:

```powershell
npm --prefix src/Cave.Ui ci
pwsh ./scripts/install-local-plugin.ps1
pwsh ./scripts/verify.ps1
```

Start a new Codex task in the target repository, review the installed hook in `/hooks`, run `/cave:init`, and then use `/cave:open`.

## Common recovery

- If the demo port is busy, pass another loopback port: `pwsh ./scripts/start-demo.ps1 -Port 5196`.
- If a frontend edit is not visible, rebuild with `npm --prefix src/Cave.Ui run build`.
- If live semantic data is unavailable, repair the CodeGraph installation or index. CAVE intentionally does not substitute demo data.
- If hooks do not emit activity, review hook trust and run `scripts/verify-live-codex-hooks.ps1` as documented in the README.

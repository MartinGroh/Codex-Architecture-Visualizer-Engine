# Demo and Media

Every asset below was captured from explicit demo mode. Names, paths, commits, activity, messages, and usage figures are synthetic.

## Walkthroughs

- [Product overview (37 seconds)](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/cave-overview.mp4)
- [Architecture drill-down (26 seconds)](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/cave-drilldown.mp4)

## Screenshots

| View | Asset |
| --- | --- |
| Live activity hero | [hero-live.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/hero-live.png) |
| System projection | [architecture-system.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/architecture-system.png) |
| Project projection | [architecture-projects.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/architecture-projects.png) |
| Class projection | [architecture-classes.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/architecture-classes.png) |
| Git changes | [git-changes.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/git-changes.png) |
| Selection details | [node-details.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/node-details.png) |
| Opt-in conversation UI | [conversation.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/conversation.png) |
| Runtime information | [info.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/info.png) |
| Dark theme | [dark-live.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/dark-live.png) |
| Narrow viewport | [mobile.png](https://github.com/MartinGroh/Codex-Architecture-Visualizer-Engine/blob/main/docs/media/mobile.png) |

## Reproduce the media

Install `ffmpeg` and use the capture script:

```powershell
pwsh ./scripts/capture-demo-media.ps1
```

The script builds the app, launches only the synthetic demo host on loopback, drives the UI through Playwright CLI, writes raw captures under ignored `output/playwright/`, converts walkthroughs to MP4, and copies reviewed artifacts into `docs/media/`.

Never capture a real workspace for public documentation. If a new UI state cannot be demonstrated with synthetic data, extend the explicit demo provider first.

# ai-stage

A fast, opinionated way to manage AI coding **sessions** across your repos and worktrees on Windows — without giving up the CLI you already like.

<p align="center">
  <img width="720" alt="ai-stage — repo and worktree session manager" src="https://github.com/user-attachments/assets/3460235c-dfd3-4479-abcc-5864ce0f82d8" />
</p>

When you open a session, you get an **Agent + CLI** pair docked side-by-side, both pinned to the same repo or worktree folder. Switch between them with a keystroke and keep working.

<p align="center">
  <img width="720" alt="ai-frame — Agent and CLI tabs in a single window" src="https://github.com/user-attachments/assets/6c620b3f-3ab1-4f53-a296-1cdeed8a4c3b" />
</p>

## Design principles

1. **Sessions, repos, and worktrees — fast.** ai-stage is a launcher and session manager. Add a repo, spin up a worktree, start a Copilot session against it, and jump back to it later. Counts on each row tell you what's already running where.
2. **Don't reinvent the terminal.** The CLI UI is already great — ai-stage doesn't try to re-render it, recolor it, or wrap it in a chat box. Each session is just a Windows Terminal hosting your Agent and a Dev Cmd shell, framed in a window so you can flip between them with `Ctrl+Tab`.

## What's in this repo

| Project | Role |
|---|---|
| **ai-stage** | The session manager (top image). Lists repos, worktrees, and active sessions. Launches `ai-frame` for each session. |
| **ai-frame** | The per-session window (bottom image). Embeds two Windows Terminal tabs — Agent and CLI — both pinned to the session folder. See [`src/ai-frame/README.md`](src/ai-frame/README.md). |
| **AgentSessions** | Shared library that discovers and tracks running agent sessions (e.g. GitHub Copilot CLI). |

## How a session works

- ai-stage launches `ai-frame.exe <folder>`.
- ai-frame opens **two Windows Terminal instances** and parents them into a single WPF window via Win32 `SetParent` — Windows Terminal still does all the rendering.
- Tab 1 runs your **Agent** (e.g. GitHub Copilot CLI). Tab 2 is a **VS Developer Command Prompt** in the same folder.
- `Ctrl+Tab` flips between them. Close the window, the session ends. Reopen it from ai-stage to come right back.

## Install

Grab the latest **`ai-stage-Setup.exe`** from the [Releases page](https://github.com/AndyGerlicher/ai-stage/releases) and run it. The installer is powered by [Velopack](https://velopack.io/), so once installed the app checks for and applies updates on its own — no need to re-download the setup for new versions.

Requires [Windows Terminal](https://aka.ms/terminal).

## Build from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [Windows Terminal](https://aka.ms/terminal).

```powershell
dotnet build
dotnet run --project src\ai-stage
```

Or launch a single session directly:

```powershell
dotnet run --project src\ai-frame -- .
```

## Status

Personal project, Windows-only, moves fast. Issues and ideas welcome.


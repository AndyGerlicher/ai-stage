# ai-frame

A lightweight app that opens two Windows Terminal tabs in a single window — one for your console and one for [GitHub Copilot CLI](https://githubnext.com/projects/copilot-cli). Both terminals open with the VS Developer Command Prompt.

## Quick Start

```
# Open ai-frame in the current directory
ai-frame.exe .

# Open ai-frame in a specific folder
ai-frame.exe D:\source\myproject

# Open ai-frame and pick a folder interactively
ai-frame.exe
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Tab` | Switch between Console and Copilot tabs |

## Explorer Integration

Right-click any folder → **Open with ai-frame**

```powershell
# Register (run once)
.\scripts\register-explorer.ps1

# Unregister
.\scripts\unregister-explorer.ps1
```

## Building

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
dotnet build
dotnet run --project src\ai-frame -- .
```

## How It Works

ai-frame launches two Windows Terminal (`wt.exe`) instances and embeds them inside a WPF window using Win32 `SetParent`. Windows Terminal handles all terminal rendering — ai-frame just provides the frame and tab switching.

Each terminal opens a VS Developer Command Prompt (`VsDevCmd.bat`), resolved automatically via `vswhere.exe`.

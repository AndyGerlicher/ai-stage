# Frame

A lightweight app that opens two Windows Terminal tabs in a single window — one for your console and one for [GitHub Copilot CLI](https://githubnext.com/projects/copilot-cli). Both terminals open with the VS Developer Command Prompt.

## Quick Start

```
# Open Frame in the current directory
Frame.exe .

# Open Frame in a specific folder
Frame.exe D:\source\myproject

# Open Frame and pick a folder interactively
Frame.exe
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Tab` | Switch between Console and Copilot tabs |

## Explorer Integration

Right-click any folder → **Open with Frame**

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
dotnet run --project src\Frame -- .
```

## How It Works

Frame launches two Windows Terminal (`wt.exe`) instances and embeds them inside a WPF window using Win32 `SetParent`. Windows Terminal handles all terminal rendering — Frame just provides the frame and tab switching.

Each terminal opens a VS Developer Command Prompt (`VsDevCmd.bat`), resolved automatically via `vswhere.exe`.

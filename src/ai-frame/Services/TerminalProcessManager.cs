using System.Diagnostics;
using System.IO;
using System.Text;
using AiFrame.Native;

namespace AiFrame.Services;

/// <summary>
/// Manages a single Windows Terminal process, launches it with a unique title marker,
/// and discovers its HWND for embedding.
/// </summary>
internal sealed class TerminalProcessManager : IDisposable
{
    private Process? _process;
    private nint _hwnd;
    private bool _disposed;
    private string? _tempScriptFile;

    public string TitleMarker { get; }
    public string WindowName { get; }
    public nint Hwnd => _hwnd;
    public bool IsAttached => _hwnd != nint.Zero;

    public TerminalProcessManager(string windowName)
    {
        TitleMarker = $"ai-frame-{Guid.NewGuid():N}";
        WindowName = windowName;
    }

    /// <summary>
    /// Launches wt.exe hosting the requested shell. For cmd-based shells we
    /// write a temp .cmd wrapper to avoid quoting hell; for PowerShell we
    /// pass the init command via -Command. Uses a unique named window and
    /// title marker so we can discover the HWND for embedding.
    /// </summary>
    public async Task<nint> LaunchAsync(string workingDirectory, string? vsDevCmdPath, TerminalLaunchSpec spec)
    {
        string shellArgs = BuildShellInvocation(spec, vsDevCmdPath);

        // Use a unique named window to guarantee a separate WT window.
        // --suppressApplicationTitle prevents the inner shell from changing the
        // title before we can discover the HWND.
        string args = $"-w {WindowName} --title \"{TitleMarker}\" --suppressApplicationTitle -d \"{workingDirectory}\" -- {shellArgs}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "wt.exe",
            Arguments = args,
            UseShellExecute = false,
        };

        _process = Process.Start(startInfo);
        if (_process is null)
            throw new InvalidOperationException("Failed to start Windows Terminal.");

        // Discover the HWND by polling for a window with our unique title marker.
        _hwnd = await DiscoverHwndAsync(TimeSpan.FromSeconds(15));

        if (_hwnd == nint.Zero)
            throw new TimeoutException(
                $"Could not find Windows Terminal window with marker '{TitleMarker}' within timeout.");

        return _hwnd;
    }

    /// <summary>
    /// Builds the trailing wt.exe argument that launches the actual shell.
    /// Returns a space-separated invocation suitable to follow the wt.exe
    /// "-- " separator.
    /// </summary>
    private string BuildShellInvocation(TerminalLaunchSpec spec, string? vsDevCmdPath)
    {
        switch (spec.Shell)
        {
            case ConsoleShell.PowerShell:
            {
                // -NoExit keeps the prompt open after the init command runs.
                if (string.IsNullOrWhiteSpace(spec.InitCommand))
                    return "powershell.exe -NoExit";

                // PowerShell single-quote escape: '' inside a '...' literal.
                string escaped = spec.InitCommand.Replace("'", "''");
                return $"powershell.exe -NoExit -Command \"& {{ {escaped} }}\"";
            }

            case ConsoleShell.Cmd:
            {
                if (string.IsNullOrWhiteSpace(spec.InitCommand))
                    return "cmd.exe /k";
                _tempScriptFile = WriteCmdWrapper(vsDevCmdPath: null, spec.InitCommand);
                return $"cmd.exe /k \"{_tempScriptFile}\"";
            }

            case ConsoleShell.VsDevCmd:
            default:
            {
                _tempScriptFile = WriteCmdWrapper(vsDevCmdPath, spec.InitCommand);
                return $"cmd.exe /k \"{_tempScriptFile}\"";
            }
        }
    }

    private async Task<nint> DiscoverHwndAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            nint hwnd = Win32.FindWindowByTitleMarker(TitleMarker);
            if (hwnd != nint.Zero)
            {
                // Hide immediately to prevent the flash of a standalone WT window
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                return hwnd;
            }

            await Task.Delay(100);
        }
        return nint.Zero;
    }

    private static string WriteCmdWrapper(string? vsDevCmdPath, string? extraCommand)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ai-frame");
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, $"ai-frame-{Guid.NewGuid():N}.cmd");

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        if (!string.IsNullOrEmpty(vsDevCmdPath))
            sb.AppendLine($"call \"{vsDevCmdPath}\" -startdir=none -arch=x64 -host_arch=x64");
        if (!string.IsNullOrEmpty(extraCommand))
            sb.AppendLine(extraCommand);
        File.WriteAllText(tempFile, sb.ToString());
        return tempFile;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Close the WT window by sending WM_CLOSE (don't detach first — that makes it visible)
        if (_hwnd != nint.Zero)
        {
            Win32.PostMessageW(_hwnd, Win32.WM_CLOSE, 0, 0);
            _hwnd = nint.Zero;
        }

        // Also try to kill the process tree if still running
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }
        _process?.Dispose();

        // Clean up temp file
        if (_tempScriptFile is not null)
        {
            try { File.Delete(_tempScriptFile); } catch { }
        }
    }
}

/// <summary>
/// Resolves the VS Developer Command Prompt path using vswhere.
/// </summary>
internal static class VsDevCmd
{
    private static string? _cachedPath;

    /// <summary>
    /// Returns the full path to VsDevCmd.bat, or null if VS isn't found.
    /// </summary>
    public static string? ResolvePath()
    {
        if (_cachedPath is not null)
            return _cachedPath;

        // Try vswhere.exe first (ships with VS Build Tools and VS)
        string vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswhere))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = vswhere,
                Arguments = "-latest -property installationPath -prerelease",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            string? installPath = proc?.StandardOutput.ReadLine()?.Trim();
            proc?.WaitForExit(5000);

            if (string.IsNullOrEmpty(installPath))
                return null;

            string devCmd = Path.Combine(installPath, "Common7", "Tools", "VsDevCmd.bat");
            if (File.Exists(devCmd))
            {
                _cachedPath = devCmd;
                return devCmd;
            }
        }
        catch
        {
            // vswhere failed, fall through
        }

        return null;
    }
}

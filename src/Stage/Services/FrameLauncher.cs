using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Stage.Services;

/// <summary>
/// Locates and launches Frame.exe, passing through an optional initial-prompt file.
/// </summary>
internal static class FrameLauncher
{
    /// <summary>
    /// Resolution order:
    /// 1. %LOCALAPPDATA%\Microsoft\WindowsApps\Frame.exe (WindowsApps alias/shim)
    /// 2. Frame.exe next to Stage.exe (sibling install)
    /// 3. Walk up from Stage.exe looking for bin\frame\Frame.exe or bin\frame.cmd
    ///    (handles both installed layout <repo>\bin\stage\Stage.exe and dev layout
    ///    <repo>\src\stage\src\Stage\bin\Debug\net10.0-windows\Stage.exe)
    /// 4. PATH search for Frame.exe then frame.cmd
    /// 5. Fallback: "Frame.exe" via shell execute
    /// </summary>
    private static string? ResolveFrame()
    {
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winApps = Path.Combine(local, "Microsoft", "WindowsApps", "Frame.exe");
            if (File.Exists(winApps)) return winApps;

            string? stageDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (stageDir is not null)
            {
                string sibling = Path.Combine(stageDir, "Frame.exe");
                if (File.Exists(sibling)) return sibling;

                // Walk up the tree looking for a bin\frame\Frame.exe or bin\frame.cmd
                var dir = new DirectoryInfo(stageDir);
                for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
                {
                    string binFrameExe = Path.Combine(dir.FullName, "bin", "frame", "Frame.exe");
                    if (File.Exists(binFrameExe)) return binFrameExe;

                    string binFrameCmd = Path.Combine(dir.FullName, "bin", "frame.cmd");
                    if (File.Exists(binFrameCmd)) return binFrameCmd;
                }
            }

            // PATH search — prefer .exe over .cmd
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string leaf in new[] { "Frame.exe", "frame.cmd" })
                {
                    foreach (string p in path.Split(Path.PathSeparator))
                    {
                        if (string.IsNullOrWhiteSpace(p)) continue;
                        try
                        {
                            string candidate = Path.Combine(p.Trim(), leaf);
                            if (File.Exists(candidate)) return candidate;
                        }
                        catch { /* skip invalid PATH entries */ }
                    }
                }
            }
        }
        catch
        {
            // fall through to shell
        }
        return null;
    }

    /// <summary>
    /// Launches Frame on a folder, optionally passing an initial-prompt file,
    /// an agent provider id (forwarded as <c>--agent &lt;id&gt;</c>), and/or
    /// the configured branch prefix (forwarded as <c>--branch-prefix &lt;value&gt;</c>)
    /// so Frame's title strip stays in sync with Stage's setting.
    /// Shows a MessageBox with install hint if launch fails.
    /// </summary>
    public static bool Launch(string folder, string? initialPromptFile = null, string? agentId = null, string? branchPrefix = null)
    {
        string? frame = ResolveFrame();

        var args = new List<string> { $"\"{folder}\"" };
        if (!string.IsNullOrEmpty(initialPromptFile))
            args.Add($"--initial-prompt-file \"{initialPromptFile}\"");
        if (!string.IsNullOrEmpty(agentId))
            args.Add($"--agent \"{agentId}\"");
        // Pass the flag whenever the caller supplied a value, including the
        // empty string — that's how Stage tells Frame "use no prefix" and
        // overrides Frame's standalone default.
        if (branchPrefix is not null)
            args.Add($"--branch-prefix \"{branchPrefix}\"");
        string argString = string.Join(' ', args);

        ProcessStartInfo startInfo;
        if (frame is not null && frame.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // .cmd files must be invoked through a shell. Use cmd.exe /c so we don't
            // need UseShellExecute=true (which would pop a console window).
            startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{frame}\" {argString}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = frame ?? "Frame.exe",
                Arguments = argString,
                UseShellExecute = frame is null,
            };
        }

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not launch Frame:\n\n{ex.Message}\n\n" +
                "Install Frame via components\\apps-frame.ps1, or make sure Frame.exe (or frame.cmd) is on your PATH.",
                "Launch Frame",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }
}

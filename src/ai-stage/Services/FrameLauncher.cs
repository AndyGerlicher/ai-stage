using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AiStage.Services;

/// <summary>
/// Locates and launches ai-frame.exe, passing through an optional initial-prompt file.
/// </summary>
internal static class FrameLauncher
{
    /// <summary>
    /// Resolution order:
    /// 1. %LOCALAPPDATA%\Microsoft\WindowsApps\ai-frame.exe (WindowsApps alias/shim)
    /// 2. ai-frame.exe next to ai-stage.exe (sibling install)
    /// 3. Walk up from ai-stage.exe looking for bin\ai-frame\ai-frame.exe or bin\ai-frame.cmd
    ///    (handles both installed layout <repo>\bin\ai-stage\ai-stage.exe and dev layout
    ///    <repo>\src\ai-stage\bin\Debug\net10.0-windows\ai-stage.exe)
    /// 4. PATH search for ai-frame.exe then ai-frame.cmd
    /// 5. Fallback: "ai-frame.exe" via shell execute
    /// </summary>
    internal static string? ResolveFrame()
    {
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string winApps = Path.Combine(local, "Microsoft", "WindowsApps", "ai-frame.exe");
            if (File.Exists(winApps)) return winApps;

            string? stageDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (stageDir is not null)
            {
                string sibling = Path.Combine(stageDir, "ai-frame.exe");
                if (File.Exists(sibling)) return sibling;

                // Walk up the tree looking for a bin\ai-frame\ai-frame.exe or bin\ai-frame.cmd
                var dir = new DirectoryInfo(stageDir);
                for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
                {
                    string binFrameExe = Path.Combine(dir.FullName, "bin", "ai-frame", "ai-frame.exe");
                    if (File.Exists(binFrameExe)) return binFrameExe;

                    string binFrameCmd = Path.Combine(dir.FullName, "bin", "ai-frame.cmd");
                    if (File.Exists(binFrameCmd)) return binFrameCmd;
                }
            }

            // PATH search — prefer .exe over .cmd
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string leaf in new[] { "ai-frame.exe", "ai-frame.cmd" })
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
    /// Launches ai-frame on a folder, optionally passing an initial-prompt file,
    /// an agent provider id (forwarded as <c>--agent &lt;id&gt;</c>),
    /// the configured branch prefix (forwarded as <c>--branch-prefix &lt;value&gt;</c>),
    /// ai-stage's per-tab customization (Console shell + init command, agent
    /// launch commands), and the preferred editor command (forwarded as
    /// <c>--preferred-editor &lt;cmd&gt;</c>). Shows a MessageBox with install
    /// hint if launch fails.
    /// </summary>
    public static bool Launch(
        string folder,
        string? initialPromptFile = null,
        string? agentId = null,
        string? branchPrefix = null,
        string? consoleShell = null,
        string? consoleInit = null,
        string? agentLaunchCommands = null,
        string? preferredEditor = null)
    {
        string? frame = ResolveFrame();

        var args = new List<string> { $"\"{folder}\"" };
        if (!string.IsNullOrEmpty(initialPromptFile))
            args.Add($"--initial-prompt-file \"{initialPromptFile}\"");
        if (!string.IsNullOrEmpty(agentId))
            args.Add($"--agent \"{agentId}\"");
        // Pass the flag whenever the caller supplied a value, including the
        // empty string — that's how ai-stage tells ai-frame "use no prefix" and
        // overrides ai-frame's standalone default.
        if (branchPrefix is not null)
            args.Add($"--branch-prefix \"{branchPrefix}\"");
        if (!string.IsNullOrEmpty(consoleShell))
            args.Add($"--console-shell \"{consoleShell}\"");
        if (!string.IsNullOrEmpty(consoleInit))
            args.Add($"--console-init \"{EscapeQuotes(consoleInit)}\"");
        // Non-null value (including empty) overrides ai-frame's fallback to
        // the provider's built-in default launch commands.
        if (agentLaunchCommands is not null)
            args.Add($"--agent-launch-commands \"{EscapeQuotes(agentLaunchCommands)}\"");
        if (!string.IsNullOrEmpty(preferredEditor))
            args.Add($"--preferred-editor \"{preferredEditor}\"");
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
                FileName = frame ?? "ai-frame.exe",
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
                $"Could not launch ai-frame:\n\n{ex.Message}\n\n" +
                "Install ai-frame via components\\apps-frame.ps1, or make sure ai-frame.exe (or ai-frame.cmd) is on your PATH.",
                "Launch ai-frame",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// Backslash-escape any embedded double quotes for safe round-trip through
    /// CommandLineToArgvW (which is what .NET uses to parse process args).
    /// </summary>
    private static string EscapeQuotes(string value) => value.Replace("\"", "\\\"");
}

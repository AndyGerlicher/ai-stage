using System.IO;
using System.Windows;
using System.Windows.Shell;
using AiFrame.Services;

namespace AiFrame;

public partial class App : Application
{
    private readonly RecentFoldersService _recentFolders = new();

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Prevent shutdown when the folder dialog closes before MainWindow opens
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        string? folder = null;
        string? initialPromptFile = null;
        string? agentId = null;
        string? agentCommandOverride = null;
        string? agentTitleOverride = null;
        string? agentArgs = null;
        string? branchPrefix = null;
        string? consoleShellRaw = null;
        string? consoleInit = null;
        string? preferredEditor = null;

        // Parse CLI args: first non-flag value is the folder.
        // --initial-prompt-file <path>     optional seed prompt
        // --agent <id>                     pick a registered agent provider (default: github-copilot)
        // --agent-command "<cmd>"          escape hatch: run a raw command, no provider lookup
        // --agent-title "<title>"          tab title override (used with --agent-command)
        // --agent-args "<args>"            extra arguments forwarded to the agent CLI (e.g. --allow-all-tools)
        // --branch-prefix <value>          override the branch prefix stripped from the window title
        // --console-shell <kind>           Console tab shell: VsDevCmd | PowerShell | Cmd
        // --console-init "<line>"          command to run in the Console tab after shell init
        // --preferred-editor <cmd>         executable for the "Open in editor" toolbar button (default: code-insiders)
        for (int i = 0; i < e.Args.Length; i++)
        {
            string arg = e.Args[i];
            if (string.Equals(arg, "--initial-prompt-file", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                initialPromptFile = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--agent", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                agentId = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--agent-command", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                agentCommandOverride = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--agent-title", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                agentTitleOverride = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--agent-args", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                agentArgs = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--branch-prefix", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                branchPrefix = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--console-shell", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                consoleShellRaw = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--console-init", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                consoleInit = e.Args[++i];
                continue;
            }
            if (string.Equals(arg, "--preferred-editor", StringComparison.OrdinalIgnoreCase)
                && i + 1 < e.Args.Length)
            {
                preferredEditor = e.Args[++i];
                continue;
            }

            if (folder is null && Directory.Exists(arg))
                folder = Path.GetFullPath(arg);
        }

        if (agentId is not null && agentCommandOverride is not null)
        {
            MessageBox.Show(
                "--agent and --agent-command are mutually exclusive. Pass one or the other.",
                "ai-frame",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // No argument — show recent folders dialog
        if (folder is null)
        {
            var recent = _recentFolders.GetRecent();

            if (recent.Count > 0)
            {
                var dialog = new RecentFolderDialog(recent);
                if (dialog.ShowDialog() == true && dialog.SelectedFolder is not null)
                {
                    folder = dialog.SelectedFolder;
                }
                else
                {
                    Shutdown();
                    return;
                }
            }
            else
            {
                // No history yet — fall back to folder picker
                var picker = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select a project folder",
                    Multiselect = false,
                };

                if (picker.ShowDialog() == true)
                {
                    folder = picker.FolderName;
                }
                else
                {
                    Shutdown();
                    return;
                }
            }
        }

        _recentFolders.Add(folder);
        UpdateJumpList();

        var window = new MainWindow
        {
            WorkingDirectory = folder,
            InitialPromptFile = initialPromptFile,
            AgentId = agentId,
            AgentCommandOverride = agentCommandOverride,
            AgentTitleOverride = agentTitleOverride,
            AgentArgs = agentArgs,
            ConsoleInitCommand = consoleInit,
        };
        if (!string.IsNullOrEmpty(preferredEditor))
            window.PreferredEditor = preferredEditor;
        if (consoleShellRaw is not null
            && Enum.TryParse<AiFrame.Services.ConsoleShell>(consoleShellRaw, ignoreCase: true, out var parsedShell))
        {
            window.ConsoleShell = parsedShell;
        }
        // Empty string is intentional ("no prefix"); only skip when the flag was absent.
        if (branchPrefix is not null)
            window.BranchPrefix = branchPrefix;
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private void UpdateJumpList()
    {
        var jumpList = new JumpList();
        string exePath = Environment.ProcessPath ?? "";

        foreach (string folder in _recentFolders.GetRecent())
        {
            string name = Path.GetFileName(folder) ?? folder;
            jumpList.JumpItems.Add(new JumpTask
            {
                Title = name,
                Description = folder,
                ApplicationPath = exePath,
                Arguments = $"\"{folder}\"",
                CustomCategory = "Recent Folders",
            });
        }

        JumpList.SetJumpList(this, jumpList);
    }
}

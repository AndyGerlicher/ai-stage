using System.Windows;
using System.Windows.Input;
using AgentSessions;
using AiFrame.Controls;
using AiFrame.Native;
using AiFrame.Services;

namespace AiFrame;

public partial class MainWindow : Window
{
    private TerminalHostControl? _consoleHost;
    private TerminalHostControl? _copilotHost;
    private int _activeTab = 1; // 0 = Console, 1 = Agent (Agent is default)

    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Optional path to a UTF-8 text file containing an initial prompt for the agent.
    /// When set and the file exists, the resolved provider's launch command consumes
    /// it on startup. Set by App.xaml.cs from the <c>--initial-prompt-file &lt;path&gt;</c>
    /// CLI flag (typically passed by ai-stage).
    /// </summary>
    public string? InitialPromptFile { get; set; }

    /// <summary>Registered agent provider id (e.g. <c>"github-copilot"</c>). Null = use default.</summary>
    public string? AgentId { get; set; }

    /// <summary>Raw command-line override. When set, overrides provider lookup entirely.</summary>
    public string? AgentCommandOverride { get; set; }

    /// <summary>Optional tab title override; falls back to the resolved provider's DisplayName.</summary>
    public string? AgentTitleOverride { get; set; }

    /// <summary>Extra arguments to forward to the agent CLI. Set by ai-stage via
    /// <c>--agent-args</c>; null = let the provider use its built-in default.</summary>
    public string? AgentArgs { get; set; }

    /// <summary>Console tab shell; default <see cref="Services.ConsoleShell.VsDevCmd"/>.</summary>
    public Services.ConsoleShell ConsoleShell { get; set; } = Services.ConsoleShell.VsDevCmd;

    /// <summary>Optional command line to execute in the Console tab after shell init.</summary>
    public string? ConsoleInitCommand { get; set; }

    /// <summary>Display name for the agent tab. Defaults to "Copilot" when unresolved.</summary>
    public string AgentDisplayName { get; private set; } = "Copilot";

    /// <summary>
    /// Prefix stripped from the branch name when building the window title.
    /// Set by ai-stage via <c>--branch-prefix</c> to keep title cosmetics in sync
    /// with ai-stage's configured prefix; defaults to no prefix (full branch name
    /// shown) for standalone ai-frame launches.
    /// </summary>
    public string BranchPrefix { get; set; } = "";

    public MainWindow()
    {
        InitializeComponent();
        Native.WindowEffects.EnableThinBorder(this);
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Resources/app-icon.png"));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Title = GetProjectTitle();
        FolderPathText.Text = WorkingDirectory;

        // Resolve provider + launch command up front so the tab title can be set
        // before terminal attach. AgentCommandOverride bypasses provider lookup.
        string command;
        if (!string.IsNullOrEmpty(AgentCommandOverride))
        {
            command = AgentCommandOverride;
            AgentDisplayName = AgentTitleOverride ?? "Agent";
        }
        else
        {
            IAgentProvider provider;
            if (string.IsNullOrEmpty(AgentId))
            {
                provider = AgentRegistry.Default;
            }
            else
            {
                provider = AgentRegistry.Get(AgentId)
                    ?? throw new InvalidOperationException(
                        $"Unknown agent provider id '{AgentId}'. " +
                        $"Registered: {string.Join(", ", AgentRegistry.Providers.Select(p => p.Id))}");
            }
            AgentDisplayName = AgentTitleOverride ?? provider.DisplayName;
            command = provider.GetLaunchCommand(InitialPromptFile, AgentArgs);
        }
        CopilotTabButton.Content = "★ " + AgentDisplayName;

        string instanceId = Guid.NewGuid().ToString("N")[..8];
        _copilotHost = new TerminalHostControl(AgentDisplayName, $"ai-frame-Agent-{instanceId}");
        _consoleHost = new TerminalHostControl("Console", $"ai-frame-Console-{instanceId}");

        // Add both (overlapping, toggle visibility)
        TerminalContainer.Children.Add(_copilotHost);
        TerminalContainer.Children.Add(_consoleHost);

        // Agent visible by default, Console hidden
        _consoleHost.Visibility = Visibility.Collapsed;

        StatusText.Text = "Starting terminals…";

        string? vsDevCmdPath = VsDevCmd.ResolvePath();

        var agentSpec = TerminalLaunchSpec.ForAgent(ConsoleShell, command);
        var consoleSpec = new TerminalLaunchSpec(ConsoleShell, ConsoleInitCommand);

        try
        {
            // Launch Agent first (primary tab), then Console — sequential to avoid WT merging
            await _copilotHost.AttachTerminalAsync(WorkingDirectory, vsDevCmdPath, agentSpec);
            await _consoleHost.AttachTerminalAsync(WorkingDirectory, vsDevCmdPath, consoleSpec);

            StatusText.Text = "";
            _copilotHost.FocusTerminal();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _consoleHost?.Dispose();
        _copilotHost?.Dispose();
    }

    #region Tab Switching

    private void OnConsoleTabClick(object sender, RoutedEventArgs e) => SwitchToTab(0);
    private void OnCopilotTabClick(object sender, RoutedEventArgs e) => SwitchToTab(1);

    private void SwitchToTab(int tab)
    {
        if (_consoleHost is null || _copilotHost is null) return;

        _activeTab = tab;

        if (tab == 0)
        {
            _consoleHost.Visibility = Visibility.Visible;
            _copilotHost.Visibility = Visibility.Collapsed;
            _consoleHost.SetVisible(true);
            _copilotHost.SetVisible(false);
            _consoleHost.FocusTerminal();
            ConsoleTabButton.Tag = "active";
            CopilotTabButton.Tag = "inactive";
        }
        else
        {
            _copilotHost.Visibility = Visibility.Visible;
            _consoleHost.Visibility = Visibility.Collapsed;
            _copilotHost.SetVisible(true);
            _consoleHost.SetVisible(false);
            _copilotHost.FocusTerminal();
            CopilotTabButton.Tag = "active";
            ConsoleTabButton.Tag = "inactive";
        }
    }

    private void ToggleTab() => SwitchToTab(_activeTab == 0 ? 1 : 0);

    #endregion

    #region Custom Title Bar — Minimize / Maximize / Close

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeButton.Content = "\uE922"; // Maximize icon
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeButton.Content = "\uE923"; // Restore icon
        }
    }

    #endregion

    #region Folder Switcher

    private void OnOpenInExplorerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{WorkingDirectory}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open Explorer:\n{ex.Message}",
                "Open in Explorer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnOpenInVSCodeInsidersClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "code-insiders",
                Arguments = $"\"{WorkingDirectory}\"",
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not launch VS Code Insiders:\n{ex.Message}",
                "Open in VS Code Insiders",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Title Helpers

    /// <summary>
    /// Builds a human-friendly project title from the working directory.
    /// For worktrees under a *.wt folder: "RepoName — branch-suffix".
    /// For regular repos: "FolderName — branch".
    /// </summary>
    private string GetProjectTitle()
    {
        string folderName = System.IO.Path.GetFileName(WorkingDirectory);
        string? parentDir = System.IO.Path.GetDirectoryName(WorkingDirectory);
        string? parentName = parentDir is not null ? System.IO.Path.GetFileName(parentDir) : null;

        // Detect worktree slot: parent dir ends with .wt
        string repoName = parentName is not null && parentName.EndsWith(".wt", StringComparison.OrdinalIgnoreCase)
            ? parentName[..^3]  // strip .wt suffix
            : folderName;

        string branch = ReadGitBranch();
        if (string.IsNullOrEmpty(branch))
            return repoName;

        // Strip the configured branch prefix for a cleaner title.
        string displayBranch = branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
            ? branch[BranchPrefix.Length..]
            : branch;

        return $"{repoName} — {displayBranch}";
    }

    /// <summary>Reads the current branch from .git/HEAD (or worktree HEAD) without spawning git.</summary>
    private string ReadGitBranch()
    {
        try
        {
            string gitPath = System.IO.Path.Combine(WorkingDirectory, ".git");

            // In a worktree, .git is a file containing "gitdir: <path>"
            string headPath;
            if (System.IO.File.Exists(gitPath))
            {
                string gitdirLine = System.IO.File.ReadAllText(gitPath).Trim();
                const string gitdirPrefix = "gitdir: ";
                if (!gitdirLine.StartsWith(gitdirPrefix, StringComparison.Ordinal))
                    return "";
                string gitdir = gitdirLine[gitdirPrefix.Length..];
                if (!System.IO.Path.IsPathRooted(gitdir))
                    gitdir = System.IO.Path.GetFullPath(System.IO.Path.Combine(WorkingDirectory, gitdir));
                headPath = System.IO.Path.Combine(gitdir, "HEAD");
            }
            else if (System.IO.Directory.Exists(gitPath))
            {
                headPath = System.IO.Path.Combine(gitPath, "HEAD");
            }
            else
            {
                return "";
            }

            if (!System.IO.File.Exists(headPath))
                return "";

            string content = System.IO.File.ReadAllText(headPath).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (content.StartsWith(refPrefix, StringComparison.Ordinal))
                return content[refPrefix.Length..];

            // Detached HEAD
            return content.Length >= 7 ? content[..7] : "";
        }
        catch
        {
            return "";
        }
    }

    #endregion
}
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AgentSessions;
using AiStage.Models;
using AiStage.Services;

namespace AiStage;

public partial class MainWindow : Window
{
    public string RootPath { get; set; } = @"D:\src";

    /// <summary>
    /// Optional agent provider id (e.g. <c>"claude-code"</c>) to forward to
    /// ai-frame via <c>--agent &lt;id&gt;</c> when opening a folder with no live
    /// session. Null = let ai-frame use its own default. When a live session is
    /// found at the path, its provider always wins over this fallback.
    /// </summary>
    public string? DefaultAgentProvider { get; set; }

    /// <summary>
    /// Prefix applied to new worktree branch names (e.g. <c>"dev/angerlic/"</c>).
    /// Loaded from <see cref="StageConfig.BranchPrefix"/>; either an empty string
    /// (no prefix) or a non-empty value ending with <c>/</c>.
    /// </summary>
    public string BranchPrefix { get; set; } = StageConfig.DefaultBranchPrefix;

    /// <summary>
    /// Name of the branch ai-stage syncs new and reset worktrees to. Loaded
    /// from <see cref="StageConfig.DefaultBranch"/>; <see cref="Services.ConfigService.Load"/>
    /// guarantees a non-empty value here (falls back to <see cref="StageConfig.DefaultBranchFallback"/>
    /// for null/empty user input).
    /// </summary>
    public string DefaultBranch { get; set; } = StageConfig.DefaultBranchFallback;

    /// <summary>
    /// Commands run by the "Reset worktree" action, one per line. Loaded from
    /// <see cref="StageConfig.WorktreeResetCommands"/>; ConfigService.Load
    /// substitutes the default if the user hasn't customized it.
    /// </summary>
    public string WorktreeResetCommands { get; set; } = StageConfig.DefaultWorktreeResetCommands;

    /// <summary>Console-tab shell choice forwarded to ai-frame (null = ai-frame default).</summary>
    public string? ConsoleShell { get; set; }

    /// <summary>Optional console-tab init command line forwarded to ai-frame.</summary>
    public string? ConsoleInitCommand { get; set; }

    /// <summary>Preferred editor command (e.g. <c>"code"</c> or <c>"code-insiders"</c>)
    /// forwarded to ai-frame; null = ai-frame's built-in default.</summary>
    public string? PreferredEditor { get; set; }

    /// <summary>Per-provider CLI argument overrides; missing key = use provider default.</summary>
    public Dictionary<string, string> AgentArgs { get; set; } = new();

    private readonly ObservableCollection<RepoNode> _repos = new();
    private readonly RepoFilter _filter = new();
    private IAgentSessionStore? _agentStore;
    private AgentSessionRowBinder? _agentBinder;

    /// <summary>
    /// Set true once the user has confirmed a "close anyway" prompt (either via the
    /// X-button close path or via the apply-update flow), so we don't double-prompt
    /// when the resulting <see cref="Window.Closing"/> event fires.
    /// </summary>
    private bool _closeGuardBypassed;

    public MainWindow()
    {
        InitializeComponent();
        Native.WindowEffects.EnableThinBorder(this);
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Resources/app-icon.png"));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = FormatVersion();
        UpdateService.StatusChanged += OnUpdateStatusChanged;
        ApplyUpdateStatus(UpdateService.CurrentStatus);

        RepoTree.ItemsSource = _repos;

        var view = CollectionViewSource.GetDefaultView(_repos);
        view.Filter = o => o is RepoNode r && r.MatchesFilter();
        _filter.Changed += (_, _) => view.Refresh();

        await RefreshAsync();

        _agentStore = AgentRegistry.CreateAggregateStore();
        _agentBinder = new AgentSessionRowBinder(_agentStore, _repos, Dispatcher);
        _agentStore.Start();

        Closed += (_, _) =>
        {
            UpdateService.StatusChanged -= OnUpdateStatusChanged;
            _agentBinder?.Dispose();
            _agentStore?.Dispose();
        };
    }

    private async Task RefreshAsync()
    {
        Title = $"ai-stage — {RootPath}";
        RootPathText.Text = RootPath;

        _repos.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        ScanningText.Visibility = Visibility.Visible;

        IReadOnlyList<RepoNode> found;
        try
        {
            found = await RepoScanner.ScanAsync(RootPath, BranchPrefix);
        }
        catch (Exception ex)
        {
            ScanningText.Visibility = Visibility.Collapsed;
            EmptyStateText.Text = $"Scan failed: {ex.Message}";
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        ScanningText.Visibility = Visibility.Collapsed;

        foreach (var r in found)
        {
            r.Filter = _filter;
            _repos.Add(r);
        }

        // Newly created node instances need to pick up live session state.
        _agentBinder?.ReapplyToCurrentRepos();

        if (_repos.Count == 0)
        {
            EmptyStateText.Text = Directory.Exists(RootPath)
                ? $"No git repositories found in {RootPath}."
                : $"Root folder not found: {RootPath}";
            EmptyState.Visibility = Visibility.Visible;
        }
    }

    // ---- Row actions ----

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe)
        {
            string? path = GetPathFromDataContext(fe.DataContext);
            if (path is not null)
            {
                OpenPath(path);
                e.Handled = true;
            }
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        string? path = GetPathFromButton(sender);
        if (path is not null)
            OpenPath(path);
    }

    /// <summary>
    /// Opens a row's folder. Focus strategy, in order:
    ///   1. If a running <c>ai-frame.exe</c> has this folder as its
    ///      positional argument, bring its top-level WPF window forward
    ///      (<see cref="FrameWindowActivator"/>). This is the common case;
    ///      it also covers ai-frame windows that opened before any agent
    ///      session was detected.
    ///   2. Else, if a live agent session targets this folder, walk the
    ///      agent process's parent chain to find the hosting terminal
    ///      window (<see cref="AgentSessionLauncher.TryFocus(AgentSession)"/>).
    ///      Serves agents running outside ai-frame (e.g. plain Windows
    ///      Terminal launched by the user).
    ///   3. Else, launch a new ai-frame. The agent id forwarded is, in
    ///      priority order:
    ///        a. the existing session's <c>ProviderId</c> (when one is found),
    ///        b. <see cref="DefaultAgentProvider"/> from ai-stage config,
    ///        c. null (ai-frame falls back to its built-in default).
    /// </summary>
    private void OpenPath(string path)
    {
        if (FrameWindowActivator.TryFocus(path))
            return;

        var session = _agentBinder?.FindSessionForPath(path);
        if (session is not null && AgentSessionLauncher.TryFocus(session))
            return;
        // Resolve which agent we're handing off (and therefore whose extra
        // args to forward). Priority: live-session provider > Stage default
        // > null (let ai-frame pick its own default).
        string? agentId = session?.ProviderId ?? DefaultAgentProvider;

        // Look up the matching extra-args override, if any. AgentArgs uses
        // missing-key = "use provider default", explicit empty string =
        // "no extra args"; FrameLauncher.Launch preserves both via the
        // null vs. "" distinction on its agentArgs parameter.
        string? agentArgs = null;
        if (agentId is not null && AgentArgs.TryGetValue(agentId, out var configured))
            agentArgs = configured;

        FrameLauncher.Launch(
            path,
            agentId: agentId,
            branchPrefix: BranchPrefix,
            consoleShell: ConsoleShell,
            consoleInit: ConsoleInitCommand,
            agentArgs: agentArgs,
            preferredEditor: PreferredEditor);
    }

    private async void OnNewWorktreeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RepoNode repo)
            return;

        string repoName = Path.GetFileName(repo.Path);
        string worktreeRoot = Path.Combine(Path.GetDirectoryName(repo.Path)!, $"{repoName}.wt");
        int slot = WorktreeService.NextSlot(worktreeRoot);

        var dlg = new WorktreeDialog(repo.Name, slot, BranchPrefix, DefaultBranch) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        string branchSuffix = dlg.BranchName;

        WorktreeResult result;
        ShowBusy($"Creating worktree (slot {slot})...");
        try
        {
            result = await WorktreeService.CreateAsync(repo.Path, BranchPrefix, branchSuffix, DefaultBranch);
        }
        finally
        {
            HideBusy();
        }

        if (!result.Success || result.Path is null)
        {
            MessageBox.Show(
                $"Could not create worktree:\n\n{result.Error}",
                "Create worktree",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string slotName = Path.GetFileName(result.Path);
        repo.Worktrees.Add(new WorktreeNode
        {
            Name = slotName,
            Path = result.Path,
            Branch = GitMetadata.ReadBranch(Path.Combine(repo.Path, ".git", "worktrees", slotName, "HEAD")),
            LastActivityUtc = DateTime.UtcNow,
            ParentRepoPath = repo.Path,
            BranchPrefix = BranchPrefix,
        });

        // For brand-new worktrees no live session can exist yet, so the agent
        // is determined entirely by the configured default. Look up its
        // matching extra-args override the same way OpenPath does.
        string? newAgentArgs = null;
        if (DefaultAgentProvider is not null && AgentArgs.TryGetValue(DefaultAgentProvider, out var configured))
            newAgentArgs = configured;

        FrameLauncher.Launch(
            result.Path,
            agentId: DefaultAgentProvider,
            branchPrefix: BranchPrefix,
            consoleShell: ConsoleShell,
            consoleInit: ConsoleInitCommand,
            agentArgs: newAgentArgs,
            preferredEditor: PreferredEditor);
    }

    private async void OnResetWorktreeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not WorktreeNode wt)
            return;

        var parent = _repos.FirstOrDefault(r =>
            string.Equals(r.Path, wt.ParentRepoPath, StringComparison.OrdinalIgnoreCase));
        if (parent is null) return;

        // Show git status and ask for confirmation before destructive reset.
        string status = await WorktreeService.GetStatusAsync(wt.Path);

        var confirm = new ConfirmDialog(
            $"Reset worktree — {wt.DisplayName} (slot {wt.Name})",
            "This will discard all changes in this worktree.",
            status,
            "Reset") { Owner = this };
        if (confirm.ShowDialog() != true)
            return;

        int slot = int.TryParse(wt.Name, out int n) ? n : 0;

        var dlg = new WorktreeDialog(parent.Name, slot, BranchPrefix, DefaultBranch, wt.DisplayName, parent.Path) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        // Resolve per-mode dispatch. NewBranch flows through the user-editable
        // command template; the other two modes route through a dedicated
        // service method that invokes `git` directly. That avoids two
        // problems:
        //   1. `git checkout -B <name>` fails when the branch is already
        //      checked out in the primary worktree (the common case for
        //      `main`). The direct path uses `--detach --force` instead.
        //   2. Branch names from the dialog ComboBox can legally contain
        //      cmd.exe metacharacters (|, <, >, &, %, ;, …). Routing them
        //      through `cmd.exe /c` like the user template does would
        //      expose us to shell injection. The direct git path passes
        //      the ref via ArgumentList so it's never re-parsed by a shell.
        WorktreeResult result;
        string targetRef = dlg.TargetRef;
        string newBranch;
        ShowBusy($"Resetting worktree to {targetRef}...");
        try
        {
            if (dlg.Mode == WorktreeResetMode.NewBranch)
            {
                newBranch = BranchPrefix + dlg.BranchName;
                result = await WorktreeService.ResetAsync(
                    wt.ParentRepoPath, wt.Path, newBranch, targetRef, WorktreeResetCommands);
            }
            else
            {
                newBranch = dlg.BranchName;
                result = await WorktreeService.ResetToRefAsync(
                    wt.ParentRepoPath, wt.Path, targetRef);
            }
        }
        finally
        {
            HideBusy();
        }

        if (!result.Success)
        {
            MessageBox.Show(
                $"Could not reset worktree:\n\n{result.Error}",
                "Reset worktree",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Replace the node in-place so the UI updates. Read the actual HEAD
        // post-reset rather than assuming we landed on `newBranch`: the
        // detached-checkout path for Existing/Default modes intentionally
        // leaves the worktree on a detached HEAD, and even the New-branch
        // path may diverge from `newBranch` if the user customized commands.
        int idx = parent.Worktrees.IndexOf(wt);
        if (idx >= 0)
        {
            string actualBranch = GitMetadata.ReadBranch(
                Path.Combine(parent.Path, ".git", "worktrees", wt.Name, "HEAD"));
            // ReadBranch returns "(unknown)" (not empty) when the HEAD file
            // is missing/unreadable — treat that as a failure to introspect
            // and fall back to the intended local branch name.
            if (string.IsNullOrEmpty(actualBranch) || actualBranch == "(unknown)")
                actualBranch = newBranch;
            parent.Worktrees[idx] = new WorktreeNode
            {
                Name = wt.Name,
                Path = wt.Path,
                Branch = actualBranch,
                LastActivityUtc = DateTime.UtcNow,
                ParentRepoPath = wt.ParentRepoPath,
                BranchPrefix = BranchPrefix,
            };
        }

        // Surface the reset slot in ai-frame: focus an existing window for
        // this folder if one is open, otherwise spin up a fresh ai-frame.
        // Mirrors the post-create flow in OnNewWorktreeClick.
        OpenPath(wt.Path);
    }
    private async void OnDeleteWorktreeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not WorktreeNode wt)
            return;

        var confirm = MessageBox.Show(
            $"Delete worktree “{wt.Name}”?\n\n" +
            $"This will permanently delete the folder:\n{wt.Path}\n\n" +
            "Any uncommitted changes will be lost.",
            "Delete worktree",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK)
            return;

        WorktreeResult result;
        ShowBusy($"Deleting worktree “{wt.Name}”…");
        try
        {
            result = await WorktreeService.DeleteAsync(wt.ParentRepoPath, wt.Path);
        }
        finally
        {
            HideBusy();
        }

        if (!result.Success)
        {
            MessageBox.Show(
                $"Could not delete worktree:\n\n{result.Error}",
                "Delete worktree",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Remove from the parent repo's worktree list in-place.
        var parent = _repos.FirstOrDefault(r =>
            string.Equals(r.Path, wt.ParentRepoPath, StringComparison.OrdinalIgnoreCase));
        parent?.Worktrees.Remove(wt);
    }

    private void ShowBusy(string message)
    {
        BusyText.Text = message;
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void HideBusy() => BusyOverlay.Visibility = Visibility.Collapsed;

    private static string? GetPathFromButton(object sender) =>
        sender is Button btn ? GetPathFromDataContext(btn.Tag) : null;

    private static string? GetPathFromDataContext(object? ctx) => ctx switch
    {
        RepoNode r => r.Path,
        WorktreeNode w => w.Path,
        _ => null,
    };

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnPickRootClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select a root folder to scan",
            Multiselect = false,
            InitialDirectory = Directory.Exists(RootPath) ? RootPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (picker.ShowDialog() != true) return;

        RootPath = picker.FolderName;
        var config = ConfigService.Load();
        config.RootPath = RootPath;
        ConfigService.Save(config);

        await RefreshAsync();
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var current = ConfigService.Load();
        // Patch the in-memory copy to reflect what MainWindow is actually using
        // for fields the user might have changed via the title-bar shortcut
        // (Change root folder) since startup.
        current.RootPath = RootPath;
        current.BranchPrefix = BranchPrefix;
        current.DefaultBranch = DefaultBranch;
        current.DefaultAgentProvider = DefaultAgentProvider;
        current.WorktreeResetCommands = WorktreeResetCommands;
        current.ConsoleShell = ConsoleShell;
        current.ConsoleInitCommand = ConsoleInitCommand;
        current.AgentArgs = AgentArgs;

        var dlg = new SettingsDialog(current) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;

        var saved = dlg.Result;
        ConfigService.Save(saved);

        // Reload via ConfigService so BranchPrefix is normalized the same
        // way startup applies it.
        var reloaded = ConfigService.Load();
        bool rootChanged = !string.Equals(RootPath, reloaded.RootPath, StringComparison.OrdinalIgnoreCase);

        RootPath = reloaded.RootPath;
        BranchPrefix = reloaded.BranchPrefix!;
        DefaultBranch = reloaded.DefaultBranch!;
        DefaultAgentProvider = reloaded.DefaultAgentProvider;
        WorktreeResetCommands = reloaded.WorktreeResetCommands ?? StageConfig.DefaultWorktreeResetCommands;
        ConsoleShell = reloaded.ConsoleShell;
        ConsoleInitCommand = reloaded.ConsoleInitCommand;
        AgentArgs = reloaded.AgentArgs ?? new Dictionary<string, string>();

        if (rootChanged)
            await RefreshAsync();
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await RefreshAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && FilterBox.IsKeyboardFocused && FilterBox.Text.Length > 0)
        {
            FilterBox.Clear();
            e.Handled = true;
        }
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs e)
    {
        _filter.Text = FilterBox.Text;
        FilterPlaceholder.Visibility = string.IsNullOrEmpty(FilterBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Formats the NBGV-injected version for the title strip:
    /// "0.2.1 41a864b" (italic in XAML). Falls back to just the version
    /// when no git hash is present (rare — e.g. ThisAssembly missing).
    /// </summary>
    private static string FormatVersion()
    {
        // ThisAssembly.AssemblyInformationalVersion is shaped like "0.2.1+41a864b0e7"
        // (or just "0.2.1" if NBGV omitted the metadata).
        string informational = ThisAssembly.AssemblyInformationalVersion;
        int plus = informational.IndexOf('+');
        if (plus < 0)
            return informational;

        string version = informational[..plus];
        string hash = informational[(plus + 1)..];
        if (hash.Length > 7) hash = hash[..7];
        return $"{version} {hash}";
    }

    // ---- Update status indicator ----

    private static readonly SolidColorBrush UpdateDotGreen = MakeFrozen(Color.FromRgb(0x3f, 0xb9, 0x50));
    private static readonly SolidColorBrush UpdateDotYellow = MakeFrozen(Color.FromRgb(0xd2, 0x99, 0x22));
    private static readonly SolidColorBrush UpdateDotError = MakeFrozen(Color.FromRgb(0x88, 0x88, 0x88));

    private static SolidColorBrush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void OnUpdateStatusChanged(object? sender, UpdateStatusInfo info) => ApplyUpdateStatus(info);

    private void ApplyUpdateStatus(UpdateStatusInfo info)
    {
        switch (info.Status)
        {
            case UpdateStatus.UpToDate:
                UpdateStatusButton.Tag = UpdateDotGreen;
                UpdateStatusButton.ToolTip = info.LastCheckedUtc is { } t
                    ? $"Up to date — last checked {t.ToLocalTime():HH:mm}"
                    : "Up to date";
                UpdateStatusButton.Cursor = null;
                UpdateStatusButton.IsEnabled = false;
                UpdateStatusButton.Visibility = Visibility.Visible;
                break;

            case UpdateStatus.UpdateAvailable:
                UpdateStatusButton.Tag = UpdateDotYellow;
                UpdateStatusButton.ToolTip = string.IsNullOrEmpty(info.AvailableVersion)
                    ? "Update available — click to install"
                    : $"Update {info.AvailableVersion} available — click to install";
                UpdateStatusButton.Cursor = Cursors.Hand;
                UpdateStatusButton.IsEnabled = true;
                UpdateStatusButton.Visibility = Visibility.Visible;
                break;

            case UpdateStatus.Error:
                UpdateStatusButton.Tag = UpdateDotError;
                UpdateStatusButton.ToolTip = string.IsNullOrEmpty(info.ErrorMessage)
                    ? "Update check failed — will retry"
                    : $"Update check failed: {info.ErrorMessage}";
                UpdateStatusButton.Cursor = null;
                UpdateStatusButton.IsEnabled = false;
                UpdateStatusButton.Visibility = Visibility.Visible;
                break;

            case UpdateStatus.NotInstalled:
                // Dev / unmanaged layout — Velopack isn't in charge so we have nothing to
                // poll for. Surface a muted dot anyway so the wiring is visible during
                // development (and the user knows why click-to-update doesn't work).
                UpdateStatusButton.Tag = UpdateDotError;
                UpdateStatusButton.ToolTip = "Dev build — updates disabled";
                UpdateStatusButton.Cursor = null;
                UpdateStatusButton.IsEnabled = false;
                UpdateStatusButton.Visibility = Visibility.Visible;
                break;

            default:
                // Unknown / Checking — keep the dot hidden so the title bar stays quiet
                // before the first check completes.
                UpdateStatusButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async void OnUpdateStatusClick(object sender, RoutedEventArgs e)
    {
        var info = UpdateService.CurrentStatus;
        if (info.Status != UpdateStatus.UpdateAvailable)
            return;

        // Hard block when ai-frame instances are running — Velopack will replace
        // ai-frame.exe alongside ai-stage.exe, and Windows holds locks on running
        // executables. Letting the user "update anyway" here would leave a partially
        // swapped install. Tell them what to close, return, and let them retry.
        var openFrames = FrameInstanceProbe.FindRunning();
        if (openFrames.Count > 0)
        {
            var detail = new StringBuilder();
            foreach (var f in openFrames)
                detail.AppendLine($"PID {f.Pid} — {f.MainWindowTitle}");

            string blockMsg = openFrames.Count == 1
                ? "Close the open ai-frame window first, then click the update indicator again."
                : $"Close the {openFrames.Count} open ai-frame windows first, then click the update indicator again.";

            MessageBox.Show(
                this,
                blockMsg + "\n\n" + detail.ToString().TrimEnd(),
                "ai-frame still running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string label = string.IsNullOrEmpty(info.AvailableVersion)
            ? "Restart ai-stage to install the latest update?"
            : $"Restart ai-stage to install version {info.AvailableVersion}?";
        var dlg = new ConfirmDialog(
            "Install update",
            label,
            "ai-stage will exit, apply the update, and relaunch on the new version.",
            confirmLabel: "Restart and update")
        { Owner = this };

        if (dlg.ShowDialog() != true) return;

        // Re-probe just before applying — a frame could have been launched while the
        // confirm prompt was up. If so, abort and let the user clean up.
        openFrames = FrameInstanceProbe.FindRunning();
        if (openFrames.Count > 0)
        {
            MessageBox.Show(
                this,
                "An ai-frame window was opened while the prompt was showing. Close it and try again.",
                "ai-frame still running",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        UpdateStatusButton.IsEnabled = false;
        try
        {
            // Stage the apply-on-exit + relaunch BEFORE flipping the close-guard or
            // shutting down — if Velopack throws, we still have a working app.
            await UpdateService.ApplyAndRestartAsync();
            _closeGuardBypassed = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusButton.IsEnabled = true;
            MessageBox.Show(
                this,
                $"Could not apply the update:\n\n{ex.Message}\n\nai-stage will retry on the next interval.",
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ---- Close guard ----

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeGuardBypassed) return;
        if (!ConfirmCloseWithFramesOpen())
        {
            e.Cancel = true;
            return;
        }
        _closeGuardBypassed = true; // any later Closing pass is a no-op
    }

    /// <summary>
    /// On a normal user-initiated close, warns when ai-frame instances spawned from this
    /// install are still running. Killing ai-stage doesn't actually break those sessions
    /// (ai-frame is independent), but it's surprising — the prompt makes the orphaning
    /// explicit. Returns true when it's safe to proceed (no frames, or user chose to
    /// close anyway), false when the user cancelled. The update flow uses a stricter,
    /// non-overridable check in <see cref="OnUpdateStatusClick"/> instead.
    /// </summary>
    private bool ConfirmCloseWithFramesOpen()
    {
        var frames = FrameInstanceProbe.FindRunning();
        if (frames.Count == 0) return true;

        var detail = new StringBuilder();
        foreach (var f in frames)
            detail.AppendLine($"PID {f.Pid} — {f.MainWindowTitle}");

        string message = frames.Count == 1
            ? "An ai-frame window is still open. Closing ai-stage will leave it running on its own."
            : $"{frames.Count} ai-frame windows are still open. Closing ai-stage will leave them running on their own.";

        var dlg = new ConfirmDialog(
            "ai-frame still running",
            message,
            detail.ToString().TrimEnd(),
            confirmLabel: "Close anyway")
        { Owner = this };

        return dlg.ShowDialog() == true;
    }
}

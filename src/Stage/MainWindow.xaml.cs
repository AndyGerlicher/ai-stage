using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using AgentSessions;
using Stage.Models;
using Stage.Services;

namespace Stage;

public partial class MainWindow : Window
{
    public string RootPath { get; set; } = @"D:\src";

    /// <summary>
    /// Optional agent provider id (e.g. <c>"claude-code"</c>) to forward to
    /// Frame via <c>--agent &lt;id&gt;</c> when opening a folder with no live
    /// session. Null = let Frame use its own default. When a live session is
    /// found at the path, its provider always wins over this fallback.
    /// </summary>
    public string? DefaultAgentProvider { get; set; }

    private readonly ObservableCollection<RepoNode> _repos = new();
    private readonly RepoFilter _filter = new();
    private IAgentSessionStore? _agentStore;
    private AgentSessionRowBinder? _agentBinder;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Resources/app-icon.png"));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
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
            _agentBinder?.Dispose();
            _agentStore?.Dispose();
        };
    }

    private async Task RefreshAsync()
    {
        Title = $"Stage — {RootPath}";
        RootPathText.Text = RootPath;

        _repos.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        ScanningText.Visibility = Visibility.Visible;

        IReadOnlyList<RepoNode> found;
        try
        {
            found = await RepoScanner.ScanAsync(RootPath);
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
    /// Opens a row's folder. If a live agent session already targets that
    /// folder, brings its terminal window to the front; otherwise launches
    /// Frame. The agent id forwarded to Frame is, in priority order:
    ///   1. the existing session's <c>ProviderId</c> (when one is found),
    ///   2. <see cref="DefaultAgentProvider"/> from Stage config,
    ///   3. null (Frame falls back to its built-in default).
    /// </summary>
    private void OpenPath(string path)
    {
        var session = _agentBinder?.FindSessionForPath(path);
        if (session is not null && AgentSessionLauncher.TryFocus(session))
            return;
        string? agentId = session?.ProviderId ?? DefaultAgentProvider;
        FrameLauncher.Launch(path, agentId: agentId);
    }

    private async void OnNewWorktreeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not RepoNode repo)
            return;

        string repoName = Path.GetFileName(repo.Path);
        string worktreeRoot = Path.Combine(Path.GetDirectoryName(repo.Path)!, $"{repoName}.wt");
        int slot = WorktreeService.NextSlot(worktreeRoot);

        var dlg = new WorktreeDialog(repo.Name, slot) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        string branchSuffix = dlg.BranchName;

        WorktreeResult result;
        ShowBusy($"Creating worktree (slot {slot})...");
        try
        {
            result = await WorktreeService.CreateAsync(repo.Path, branchSuffix);
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
        });

        FrameLauncher.Launch(result.Path);
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
            "This will discard all changes and reset to origin/main.",
            status,
            "Reset") { Owner = this };
        if (confirm.ShowDialog() != true)
            return;

        int slot = int.TryParse(wt.Name, out int n) ? n : 0;

        var dlg = new WorktreeDialog(parent.Name, slot, wt.DisplayName) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        string branchSuffix = dlg.BranchName;

        WorktreeResult result;
        ShowBusy("Resetting worktree to origin/main...");
        try
        {
            result = await WorktreeService.ResetAsync(wt.ParentRepoPath, wt.Path, branchSuffix);
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

        // Replace the node in-place so the UI updates.
        int idx = parent.Worktrees.IndexOf(wt);
        if (idx >= 0)
        {
            parent.Worktrees[idx] = new WorktreeNode
            {
                Name = wt.Name,
                Path = wt.Path,
                Branch = $"dev/angerlic/{branchSuffix}",
                LastActivityUtc = DateTime.UtcNow,
                ParentRepoPath = wt.ParentRepoPath,
            };
        }
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
}

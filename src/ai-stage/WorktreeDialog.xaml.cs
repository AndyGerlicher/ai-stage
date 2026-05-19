using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AiStage.Services;

namespace AiStage;

/// <summary>
/// Result mode emitted by <see cref="WorktreeDialog"/> after the user
/// confirms. <see cref="NewBranch"/> creates a fresh branch off the
/// default branch; <see cref="ExistingBranch"/> checks out a remote
/// branch the user picked; <see cref="DefaultBranch"/> checks out the
/// configured default branch directly.
/// </summary>
public enum WorktreeResetMode
{
    NewBranch,
    ExistingBranch,
    DefaultBranch,
}

public partial class WorktreeDialog : Window
{
    /// <summary>
    /// Local branch name to land on after the operation. For
    /// <see cref="WorktreeResetMode.NewBranch"/> this is the user-typed
    /// suffix (without prefix); the caller is expected to prepend
    /// <see cref="StageConfig.BranchPrefix"/>. For the other modes this is
    /// the selected remote branch name as-is.
    /// </summary>
    public string BranchName { get; private set; } = "";

    /// <summary>Selected reset mode (always <see cref="WorktreeResetMode.NewBranch"/> in create mode).</summary>
    public WorktreeResetMode Mode { get; private set; } = WorktreeResetMode.NewBranch;

    /// <summary>Git ref to reset onto (e.g. <c>origin/main</c>). Always <c>origin/&lt;something&gt;</c>.</summary>
    public string TargetRef { get; private set; } = "";

    private readonly string _repoName;
    private readonly int _slot;
    private readonly bool _isReset;
    private readonly string _branchPrefix;
    private readonly string _defaultBranch;
    private readonly string? _repoPath;
    private bool _remoteBranchesLoaded;

    /// <summary>Create mode — auto-numbered slot, user picks branch name.</summary>
    public WorktreeDialog(string repoName, int slot, string branchPrefix, string defaultBranch)
    {
        InitializeComponent();
        AiStage.Native.WindowEffects.EnableThinBorder(this);
        _repoName = repoName;
        _slot = slot;
        _isReset = false;
        _branchPrefix = branchPrefix;
        _defaultBranch = string.IsNullOrWhiteSpace(defaultBranch)
            ? StageConfig.DefaultBranchFallback : defaultBranch;
        _repoPath = null;
        TitleText.Text = $"New worktree — {repoName}";
        CreateButton.Content = "Create";
        // Create flow only supports the "new branch" path; hide the mode
        // picker entirely so the dialog looks like the old single-input one.
        ModePanel.Visibility = Visibility.Collapsed;
        NameBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Reset mode — existing slot, user picks one of three branch targets.</summary>
    public WorktreeDialog(string repoName, int slot, string branchPrefix, string defaultBranch,
        string currentBranchSuffix, string mainRepoPath)
    {
        InitializeComponent();
        AiStage.Native.WindowEffects.EnableThinBorder(this);
        _repoName = repoName;
        _slot = slot;
        _isReset = true;
        _branchPrefix = branchPrefix;
        _defaultBranch = string.IsNullOrWhiteSpace(defaultBranch)
            ? StageConfig.DefaultBranchFallback : defaultBranch;
        _repoPath = mainRepoPath;
        TitleText.Text = $"Reset worktree — {repoName} (slot {slot})";
        CreateButton.Content = "Reset";
        ModePanel.Visibility = Visibility.Visible;
        NameBox.Text = currentBranchSuffix;
        DefaultBranchText.Text = _defaultBranch;
        NameBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        // Radios fire Checked before the visual tree is fully wired during
        // InitializeComponent; guard against that.
        if (NewBranchPanel is null) return;

        if (ModeNewBranchRadio.IsChecked == true)
        {
            NewBranchPanel.Visibility = Visibility.Visible;
            ExistingBranchPanel.Visibility = Visibility.Collapsed;
            DefaultBranchPanel.Visibility = Visibility.Collapsed;
            NameBox.Focus();
        }
        else if (ModeExistingBranchRadio.IsChecked == true)
        {
            NewBranchPanel.Visibility = Visibility.Collapsed;
            ExistingBranchPanel.Visibility = Visibility.Visible;
            DefaultBranchPanel.Visibility = Visibility.Collapsed;
            _ = EnsureRemoteBranchesLoadedAsync();
        }
        else if (ModeDefaultBranchRadio.IsChecked == true)
        {
            NewBranchPanel.Visibility = Visibility.Collapsed;
            ExistingBranchPanel.Visibility = Visibility.Collapsed;
            DefaultBranchPanel.Visibility = Visibility.Visible;
        }
        UpdateHint();
    }

    private async Task EnsureRemoteBranchesLoadedAsync()
    {
        if (_remoteBranchesLoaded || string.IsNullOrEmpty(_repoPath)) return;
        _remoteBranchesLoaded = true;

        ExistingBranchLoadingText.Visibility = Visibility.Visible;
        IReadOnlyList<string> branches;
        try
        {
            branches = await WorktreeService.ListRemoteBranchesAsync(_repoPath);
        }
        catch
        {
            branches = Array.Empty<string>();
        }
        finally
        {
            ExistingBranchLoadingText.Visibility = Visibility.Collapsed;
        }

        ExistingBranchCombo.ItemsSource = branches;
        // Pre-select the default branch if it's in the list, so the typical
        // case of "reset to main" is one click away even in this mode.
        foreach (string b in branches)
        {
            if (string.Equals(b, _defaultBranch, StringComparison.OrdinalIgnoreCase))
            {
                ExistingBranchCombo.SelectedItem = b;
                break;
            }
        }
        UpdateHint();
    }

    private void OnExistingBranchChanged(object sender, SelectionChangedEventArgs e) => UpdateHint();
    private void OnExistingBranchTextChanged(object sender, KeyEventArgs e) => UpdateHint();

    private string CurrentExistingBranch()
    {
        if (ExistingBranchCombo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
            return s.Trim();
        return (ExistingBranchCombo.Text ?? "").Trim();
    }

    private void UpdateHint()
    {
        if (PathHintText is null) return;

        // Pick the active mode (create flow is always NewBranch).
        WorktreeResetMode mode = !_isReset || ModeNewBranchRadio?.IsChecked == true
            ? WorktreeResetMode.NewBranch
            : ModeExistingBranchRadio?.IsChecked == true
                ? WorktreeResetMode.ExistingBranch
                : WorktreeResetMode.DefaultBranch;

        switch (mode)
        {
            case WorktreeResetMode.NewBranch:
            {
                string name = NameBox.Text.Trim();
                string display = string.IsNullOrEmpty(name) ? "<name>" : name;
                PathHintText.Text = _isReset
                    ? $"Branch: {_branchPrefix}{display}  —  based on origin/{_defaultBranch}"
                    : $"…\\{_repoName}.wt\\{_slot}  →  branch {_branchPrefix}{display}  (origin/{_defaultBranch})";
                break;
            }
            case WorktreeResetMode.ExistingBranch:
            {
                string sel = CurrentExistingBranch();
                string display = string.IsNullOrEmpty(sel) ? "<pick a branch>" : sel;
                PathHintText.Text = $"Checkout: {display}  —  resets to origin/{display}";
                break;
            }
            case WorktreeResetMode.DefaultBranch:
                PathHintText.Text = $"Checkout: {_defaultBranch}  —  resets to origin/{_defaultBranch}";
                break;
        }
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        // Resolve mode + branch + target ref into the public properties so
        // the caller can drive WorktreeService.ResetAsync without needing to
        // know about the dialog's controls.
        WorktreeResetMode mode = !_isReset || ModeNewBranchRadio.IsChecked == true
            ? WorktreeResetMode.NewBranch
            : ModeExistingBranchRadio.IsChecked == true
                ? WorktreeResetMode.ExistingBranch
                : WorktreeResetMode.DefaultBranch;

        switch (mode)
        {
            case WorktreeResetMode.NewBranch:
            {
                string name = NameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(this, "Branch name cannot be empty.", "ai-stage",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    NameBox.Focus();
                    return;
                }
                BranchName = name;
                TargetRef = $"origin/{_defaultBranch}";
                break;
            }
            case WorktreeResetMode.ExistingBranch:
            {
                string sel = CurrentExistingBranch();
                if (string.IsNullOrWhiteSpace(sel))
                {
                    MessageBox.Show(this, "Pick an existing remote branch (or switch to a different mode).",
                        "ai-stage", MessageBoxButton.OK, MessageBoxImage.Information);
                    ExistingBranchCombo.Focus();
                    return;
                }
                BranchName = sel;
                TargetRef = $"origin/{sel}";
                break;
            }
            case WorktreeResetMode.DefaultBranch:
            {
                BranchName = _defaultBranch;
                TargetRef = $"origin/{_defaultBranch}";
                break;
            }
        }
        Mode = mode;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            OnCreateClick(sender, e);
            e.Handled = true;
        }
    }
}

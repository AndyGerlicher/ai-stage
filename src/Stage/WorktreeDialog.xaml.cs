using System.IO;
using System.Windows;
using System.Windows.Input;

namespace Stage;

public partial class WorktreeDialog : Window
{
    public string BranchName => NameBox.Text.Trim();

    private readonly string _repoName;
    private readonly int _slot;
    private readonly bool _isReset;
    private readonly string _branchPrefix;

    /// <summary>Create mode — auto-numbered slot, user picks branch name.</summary>
    public WorktreeDialog(string repoName, int slot, string branchPrefix)
    {
        InitializeComponent();
        _repoName = repoName;
        _slot = slot;
        _isReset = false;
        _branchPrefix = branchPrefix;
        TitleText.Text = $"New worktree — {repoName}";
        CreateButton.Content = "Create";
        NameBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Reset mode — existing slot, user picks new branch name.</summary>
    public WorktreeDialog(string repoName, int slot, string branchPrefix, string currentBranchSuffix)
    {
        InitializeComponent();
        _repoName = repoName;
        _slot = slot;
        _isReset = true;
        _branchPrefix = branchPrefix;
        TitleText.Text = $"Reset worktree — {repoName} (slot {slot})";
        CreateButton.Content = "Reset";
        NameBox.Text = currentBranchSuffix;
        NameBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void UpdateHint()
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            PathHintText.Text = _isReset
                ? $"Branch: {_branchPrefix}<name>  —  resets to origin/main"
                : $"…\\{_repoName}.wt\\{_slot}  →  branch {_branchPrefix}<name>";
            return;
        }
        PathHintText.Text = _isReset
            ? $"Branch: {_branchPrefix}{name}  —  resets to origin/main"
            : $"…\\{_repoName}.wt\\{_slot}  →  branch {_branchPrefix}{name}";
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BranchName))
        {
            MessageBox.Show(this, "Branch name cannot be empty.", "Stage",
                MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }
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

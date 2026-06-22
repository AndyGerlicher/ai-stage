using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AiStage;

public partial class CloneDialog : Window
{
    private readonly string _rootPath;

    /// <summary>Repository URL to clone (trimmed, never empty once the dialog confirms).</summary>
    public string Url { get; private set; } = "";

    /// <summary>Optional folder name to clone into; empty means use git's default.</summary>
    public string FolderName { get; private set; } = "";

    public CloneDialog(string rootPath)
    {
        InitializeComponent();
        AiStage.Native.WindowEffects.EnableThinBorder(this);
        _rootPath = rootPath;
        UrlBox.TextChanged += (_, _) => UpdateHint();
        NameBox.TextChanged += (_, _) => UpdateHint();
        UpdateHint();
        Loaded += (_, _) => UrlBox.Focus();
    }

    private void UpdateHint()
    {
        if (PathHintText is null) return;

        string folder = NameBox.Text.Trim();
        if (folder.Length == 0)
            folder = DeriveFolderName(UrlBox.Text);
        if (folder.Length == 0)
            folder = "<folder>";

        PathHintText.Text = $"Clones into  {Path.Combine(_rootPath, folder)}";
    }

    /// <summary>
    /// Mirrors git's default destination naming: the last path segment of the
    /// URL with a trailing <c>.git</c> removed. Handles both URL
    /// (<c>https://host/owner/repo.git</c>) and scp-like
    /// (<c>git@host:owner/repo.git</c>) forms.
    /// </summary>
    private static string DeriveFolderName(string url)
    {
        string s = url.Trim().TrimEnd('/');
        if (s.Length == 0) return "";

        int sep = s.LastIndexOfAny(new[] { '/', ':' });
        string last = sep >= 0 ? s[(sep + 1)..] : s;
        if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            last = last[..^4];
        return last.Trim();
    }

    private void OnCloneClick(object sender, RoutedEventArgs e)
    {
        string url = UrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Repository URL cannot be empty.", "ai-stage",
                MessageBoxButton.OK, MessageBoxImage.Information);
            UrlBox.Focus();
            return;
        }

        Url = url;
        FolderName = NameBox.Text.Trim();
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
            OnCloneClick(sender, e);
            e.Handled = true;
        }
    }
}

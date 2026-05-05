using System.IO;
using System.Windows;
using System.Windows.Input;

namespace AiFrame;

public partial class RecentFolderDialog : Window
{
    /// <summary>
    /// The folder selected by the user, or null if cancelled.
    /// </summary>
    public string? SelectedFolder { get; private set; }

    public RecentFolderDialog(IEnumerable<string> recentFolders)
    {
        InitializeComponent();

        var items = recentFolders
            .Where(Directory.Exists)
            .Select(f => new FolderItem(Path.GetFileName(f) ?? f, f))
            .ToList();

        FolderList.ItemsSource = items;

        if (items.Count > 0)
            FolderList.SelectedIndex = 0;
    }

    private void OnFolderDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AcceptSelection();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select a project folder",
            Multiselect = false,
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedFolder = dialog.FolderName;
            DialogResult = true;
            Close();
        }
    }

    private void AcceptSelection()
    {
        if (FolderList.SelectedItem is FolderItem item)
        {
            SelectedFolder = item.Path;
            DialogResult = true;
            Close();
        }
    }

    internal record FolderItem(string Name, string Path);
}

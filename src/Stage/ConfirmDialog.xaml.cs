using System.Windows;
using System.Windows.Input;

namespace Stage;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string detail, string confirmLabel = "Continue")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        DetailText.Text = string.IsNullOrWhiteSpace(detail) ? "(clean)" : detail;
        ConfirmButton.Content = confirmLabel;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
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
    }
}

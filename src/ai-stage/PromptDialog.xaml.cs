using System.Windows;
using System.Windows.Input;

namespace AiStage;

public partial class PromptDialog : Window
{
    public string? Prompt => string.IsNullOrWhiteSpace(PromptBox.Text) ? null : PromptBox.Text;

    public PromptDialog(string targetName)
    {
        InitializeComponent();
        TitleText.Text = $"Open with initial prompt — {targetName}";
        Loaded += (_, _) => PromptBox.Focus();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
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
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnOpenClick(sender, e);
            e.Handled = true;
        }
    }
}

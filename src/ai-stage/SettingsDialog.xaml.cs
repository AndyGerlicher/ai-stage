using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AgentSessions;
using AiStage.Services;

namespace AiStage;

public partial class SettingsDialog : Window
{
    private sealed record AgentChoice(string Id, string DisplayName);
    private sealed record ShellChoice(string Value, string Label);

    /// <summary>One row in the per-provider CLI flags editor.</summary>
    private sealed class AgentArgsRow
    {
        public required string ProviderId { get; init; }
        public required string DisplayName { get; init; }
        public required string DefaultExtraArgs { get; init; }
        public string Value { get; set; } = "";
    }

    private List<AgentArgsRow> _agentArgsRows = new();

    /// <summary>
    /// The settings the user accepted, or null if they cancelled. Set when
    /// <see cref="OnSaveClick"/> succeeds.
    /// </summary>
    internal StageConfig? Result { get; private set; }

    internal SettingsDialog(StageConfig current)
    {
        InitializeComponent();
        AiStage.Native.WindowEffects.EnableThinBorder(this);

        RootPathBox.Text = current.RootPath ?? "";
        BranchPrefixBox.Text = current.BranchPrefix ?? "";

        // Reset commands: show user value if customized, otherwise the default
        // so first-run users see the baseline they're editing from.
        ResetCommandsBox.Text = current.WorktreeResetCommands ?? StageConfig.DefaultWorktreeResetCommands;

        ConsoleInitBox.Text = current.ConsoleInitCommand ?? "";

        // Build a row per registered provider for the per-agent CLI flags
        // editor. Each row pre-fills from current.AgentArgs if the user has
        // ever saved a value for that provider; otherwise from the provider's
        // built-in default. We distinguish "missing key (use default)" from
        // "explicit empty string (no extra args)" on save.
        _agentArgsRows = new List<AgentArgsRow>();
        foreach (var p in AgentRegistry.Providers)
        {
            string value = current.AgentArgs.TryGetValue(p.Id, out var saved) ? saved : p.DefaultExtraArgs;
            _agentArgsRows.Add(new AgentArgsRow
            {
                ProviderId = p.Id,
                DisplayName = p.DisplayName,
                DefaultExtraArgs = p.DefaultExtraArgs,
                Value = value,
            });
        }
        AgentArgsList.ItemsSource = _agentArgsRows;

        var shells = new List<ShellChoice>
        {
            new("VsDevCmd",   "VS Developer Command Prompt (default)"),
            new("Pwsh", "PowerShell"),
            new("PowerShell", "PowerShell (old)"),
            new("Cmd",        "Cmd (no VsDevCmd)"),
        };
        ConsoleShellCombo.ItemsSource = shells;
        ConsoleShellCombo.SelectedValue = current.ConsoleShell ?? "VsDevCmd";
        if (ConsoleShellCombo.SelectedIndex < 0)
            ConsoleShellCombo.SelectedIndex = 0;

        // Build the agent dropdown from the registry. Prepend a "(use default)"
        // entry mapped to a null id so users can opt out of forcing a specific
        // provider — ai-frame falls back to its own built-in default.
        var choices = new List<AgentChoice>
        {
            new(string.Empty, "(use ai-frame default)"),
        };
        foreach (var p in AgentRegistry.Providers)
            choices.Add(new AgentChoice(p.Id, p.DisplayName));

        AgentCombo.ItemsSource = choices;
        AgentCombo.SelectedValue = current.DefaultAgentProvider ?? string.Empty;
        if (AgentCombo.SelectedIndex < 0)
            AgentCombo.SelectedIndex = 0;

        Loaded += (_, _) => RootPathBox.Focus();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select a starting folder",
            Multiselect = false,
            InitialDirectory = Directory.Exists(RootPathBox.Text) ? RootPathBox.Text : null,
        };

        if (picker.ShowDialog() == true)
            RootPathBox.Text = picker.FolderName;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string root = (RootPathBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(root))
        {
            MessageBox.Show(this, "Starting folder cannot be empty.", "ai-stage",
                MessageBoxButton.OK, MessageBoxImage.Information);
            RootPathBox.Focus();
            return;
        }
        if (!Directory.Exists(root))
        {
            var pick = MessageBox.Show(this,
                $"The starting folder doesn't exist:\n\n{root}\n\nSave anyway?",
                "ai-stage",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);
            if (pick != MessageBoxResult.OK) { RootPathBox.Focus(); return; }
        }

        // ConfigService.Load normalizes BranchPrefix on read; we save the raw
        // user value here and let the next Load() pass apply normalization.
        string? selectedAgentId = AgentCombo.SelectedValue as string;
        string? selectedShell = ConsoleShellCombo.SelectedValue as string;

        // Reset commands: persist null if the user left it equal to the
        // default, so future default changes naturally take effect.
        string resetCommands = (ResetCommandsBox.Text ?? "").Trim();
        string? persistedResetCommands =
            string.Equals(resetCommands, StageConfig.DefaultWorktreeResetCommands.Trim(), StringComparison.Ordinal)
                ? null
                : ResetCommandsBox.Text;

        // Per-provider CLI flag overrides: persist a row only when the user's
        // value differs from the provider's default. Blank with default = ""
        // is meaningful ("no extra args") so we still persist it.
        var agentArgs = new Dictionary<string, string>();
        foreach (var row in _agentArgsRows)
        {
            // The TextBox writes back through the ItemsControl binding so
            // row.Value already reflects the latest text.
            string val = row.Value ?? "";
            if (string.Equals(val, row.DefaultExtraArgs, StringComparison.Ordinal))
                continue; // user kept the default — don't persist, lets future default changes flow through
            agentArgs[row.ProviderId] = val;
        }

        Result = new StageConfig
        {
            RootPath = root,
            BranchPrefix = BranchPrefixBox.Text ?? string.Empty,
            DefaultAgentProvider = string.IsNullOrEmpty(selectedAgentId) ? null : selectedAgentId,
            WorktreeResetCommands = persistedResetCommands,
            ConsoleShell = string.IsNullOrEmpty(selectedShell) || selectedShell == "VsDevCmd" ? null : selectedShell,
            ConsoleInitCommand = string.IsNullOrWhiteSpace(ConsoleInitBox.Text) ? null : ConsoleInitBox.Text,
            AgentArgs = agentArgs,
        };

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

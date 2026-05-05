using System.IO;

namespace AgentSessions.Providers.ClaudeCode;

/// <summary>
/// Agent provider for the Claude Code CLI (<c>claude</c>).
/// </summary>
public sealed class ClaudeCodeProvider : IAgentProvider
{
    public const string ProviderId = "claude-code";

    /// <summary>Default arguments appended after <c>claude</c> when the host
    /// doesn't specify <paramref name="extraArgs"/>. <c>--dangerously-skip-permissions</c>
    /// is Claude Code's equivalent of GitHub Copilot's <c>--allow-all-tools</c>:
    /// it suppresses the per-tool permission prompts.</summary>
    public const string ClaudeDefaultExtraArgs = "--dangerously-skip-permissions";

    public string Id => ProviderId;
    public string DisplayName => "Claude Code";
    public string DefaultExtraArgs => ClaudeDefaultExtraArgs;

    /// <summary>
    /// Returns the command ai-frame should run to start the Claude Code CLI.
    /// When <paramref name="initialPromptFile"/> is supplied and exists, the
    /// command is wrapped in a PowerShell shim that reads the prompt and
    /// passes it as Claude's positional initial-message argument, deleting
    /// the file afterwards so each prompt is consumed exactly once.
    /// <para>
    /// <paramref name="extraArgs"/> overrides <see cref="ClaudeDefaultExtraArgs"/>;
    /// pass empty string to drop the default flags entirely.
    /// </para>
    /// </summary>
    public string GetLaunchCommand(string? initialPromptFile, string? extraArgs = null)
    {
        string args = extraArgs ?? ClaudeDefaultExtraArgs;
        string baseCmd = string.IsNullOrEmpty(args) ? "claude" : $"claude {args}";

        if (string.IsNullOrEmpty(initialPromptFile) || !File.Exists(initialPromptFile))
            return baseCmd;

        // PowerShell single-quote string escape: '' escapes a single quote.
        string escapedPath = initialPromptFile.Replace("'", "''");

        string psCommand =
            $"$p = Get-Content -Raw -LiteralPath '{escapedPath}'; " +
            $"Remove-Item -LiteralPath '{escapedPath}' -ErrorAction SilentlyContinue; " +
            $"{baseCmd} $p";

        return $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
    }

    public IAgentSessionStore CreateSessionStore() => new ClaudeCodeSessionMonitor();
}

using System.IO;

namespace AgentSessions.Providers.ClaudeCode;

/// <summary>
/// Agent provider for the Claude Code CLI (<c>claude</c>).
/// </summary>
public sealed class ClaudeCodeProvider : IAgentProvider
{
    public const string ProviderId = "claude-code";

    public string Id => ProviderId;
    public string DisplayName => "Claude Code";

    /// <summary>
    /// Returns the command Frame should run to start the Claude Code CLI.
    /// When <paramref name="initialPromptFile"/> is supplied and exists, the
    /// command is wrapped in a PowerShell shim that reads the prompt and
    /// passes it as Claude's positional initial-message argument, deleting
    /// the file afterwards so each prompt is consumed exactly once.
    /// </summary>
    public string GetLaunchCommand(string? initialPromptFile)
    {
        const string defaultCmd = "claude";

        if (string.IsNullOrEmpty(initialPromptFile) || !File.Exists(initialPromptFile))
            return defaultCmd;

        // PowerShell single-quote string escape: '' escapes a single quote.
        string escapedPath = initialPromptFile.Replace("'", "''");

        string psCommand =
            $"$p = Get-Content -Raw -LiteralPath '{escapedPath}'; " +
            $"Remove-Item -LiteralPath '{escapedPath}' -ErrorAction SilentlyContinue; " +
            "claude $p";

        return $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
    }

    public IAgentSessionStore CreateSessionStore() => new ClaudeCodeSessionMonitor();
}

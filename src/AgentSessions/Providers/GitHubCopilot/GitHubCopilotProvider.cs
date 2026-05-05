using System.IO;

namespace AgentSessions.Providers.GitHubCopilot;

/// <summary>
/// The default agent provider: the GitHub Copilot CLI (<c>copilot</c>).
/// </summary>
public sealed class GitHubCopilotProvider : IAgentProvider
{
    public const string ProviderId = "github-copilot";

    public string Id => ProviderId;
    public string DisplayName => "Copilot";

    /// <summary>
    /// Returns the command ai-frame should run to start the GitHub Copilot CLI.
    /// When <paramref name="initialPromptFile"/> is supplied and exists, the
    /// command is wrapped in a PowerShell shim that reads the prompt and
    /// passes it via <c>-i</c>, deleting the file afterwards so each prompt
    /// is consumed exactly once.
    /// </summary>
    public string GetLaunchCommand(string? initialPromptFile)
    {
        const string defaultCmd = "copilot --allow-all-tools";

        if (string.IsNullOrEmpty(initialPromptFile) || !File.Exists(initialPromptFile))
            return defaultCmd;

        // PowerShell single-quote string escape: '' escapes a single quote.
        string escapedPath = initialPromptFile.Replace("'", "''");

        string psCommand =
            $"$p = Get-Content -Raw -LiteralPath '{escapedPath}'; " +
            $"Remove-Item -LiteralPath '{escapedPath}' -ErrorAction SilentlyContinue; " +
            "copilot --allow-all-tools -i $p";

        return $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
    }

    public IAgentSessionStore CreateSessionStore() => new GitHubCopilotSessionMonitor();
}

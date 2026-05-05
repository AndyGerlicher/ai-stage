using System.IO;

namespace AgentSessions.Providers.GitHubCopilot;

/// <summary>
/// The default agent provider: the GitHub Copilot CLI (<c>copilot</c>).
/// </summary>
public sealed class GitHubCopilotProvider : IAgentProvider
{
    public const string ProviderId = "github-copilot";

    /// <summary>Default arguments appended after <c>copilot</c> when the host
    /// doesn't specify <paramref name="extraArgs"/>.</summary>
    public const string CopilotDefaultExtraArgs = "--allow-all-tools";

    public string Id => ProviderId;
    public string DisplayName => "Copilot";
    public string DefaultExtraArgs => CopilotDefaultExtraArgs;

    /// <summary>
    /// Returns the command ai-frame should run to start the GitHub Copilot CLI.
    /// When <paramref name="initialPromptFile"/> is supplied and exists, the
    /// command is wrapped in a PowerShell shim that reads the prompt and
    /// passes it via <c>-i</c>, deleting the file afterwards so each prompt
    /// is consumed exactly once.
    /// <para>
    /// <paramref name="extraArgs"/> overrides <see cref="CopilotDefaultExtraArgs"/>;
    /// pass empty string to drop the default flags entirely.
    /// </para>
    /// </summary>
    public string GetLaunchCommand(string? initialPromptFile, string? extraArgs = null)
    {
        string args = extraArgs ?? CopilotDefaultExtraArgs;
        string baseCmd = string.IsNullOrEmpty(args) ? "copilot" : $"copilot {args}";

        if (string.IsNullOrEmpty(initialPromptFile) || !File.Exists(initialPromptFile))
            return baseCmd;

        // PowerShell single-quote string escape: '' escapes a single quote.
        string escapedPath = initialPromptFile.Replace("'", "''");

        string psCommand =
            $"$p = Get-Content -Raw -LiteralPath '{escapedPath}'; " +
            $"Remove-Item -LiteralPath '{escapedPath}' -ErrorAction SilentlyContinue; " +
            $"{baseCmd} -i $p";

        return $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
    }

    public IAgentSessionStore CreateSessionStore() => new GitHubCopilotSessionMonitor();
}

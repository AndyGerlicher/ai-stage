using System.IO;
using AgentSessions.Internal;

namespace AgentSessions.Providers.GitHubCopilot;

/// <summary>
/// The default agent provider: the GitHub Copilot CLI (<c>copilot</c>).
/// </summary>
public sealed class GitHubCopilotProvider : IAgentProvider
{
    public const string ProviderId = "github-copilot";

    /// <summary>Default launch script when the user hasn't customized it.</summary>
    public const string CopilotDefaultLaunchCommands = "copilot --allow-all-tools";

    public string Id => ProviderId;
    public string DisplayName => "Copilot";
    public string DefaultLaunchCommands => CopilotDefaultLaunchCommands;

    /// <summary>
    /// Builds the command ai-frame runs in the agent terminal.
    /// Non-empty / non-<c>#</c>-comment lines from <paramref name="commands"/>
    /// are chained with <c>&amp;&amp;</c> in order; the last line is the
    /// persistent interactive Copilot process.
    /// <para>
    /// When <paramref name="initialPromptFile"/> is supplied and exists, the
    /// final line is wrapped in a PowerShell shim that reads the prompt and
    /// passes it via <c>-i</c>, deleting the file afterwards so each prompt
    /// is consumed exactly once. Earlier lines (e.g. <c>copilot update</c>)
    /// run unmodified.
    /// </para>
    /// </summary>
    public string BuildLaunchCommand(string? initialPromptFile, string commands)
    {
        var lines = LaunchCommandComposer.ParseLines(commands);
        if (lines.Count == 0)
            return "";

        if (string.IsNullOrEmpty(initialPromptFile) || !File.Exists(initialPromptFile))
            return LaunchCommandComposer.Chain(lines);

        string preamble = LaunchCommandComposer.ChainPreamble(lines);
        string finalCmd = lines[^1];

        // PowerShell single-quote string escape: '' escapes a single quote.
        string escapedPath = initialPromptFile.Replace("'", "''");

        string psCommand =
            $"$p = Get-Content -Raw -LiteralPath '{escapedPath}'; " +
            $"Remove-Item -LiteralPath '{escapedPath}' -ErrorAction SilentlyContinue; " +
            $"{finalCmd} -i $p";

        return preamble + $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
    }

    public IAgentSessionStore CreateSessionStore() => new GitHubCopilotSessionMonitor();
}

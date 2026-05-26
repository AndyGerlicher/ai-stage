using System.IO;
using AgentSessions.Internal;

namespace AgentSessions.Providers.ClaudeCode;

/// <summary>
/// Agent provider for the Claude Code CLI (<c>claude</c>).
/// </summary>
public sealed class ClaudeCodeProvider : IAgentProvider
{
    public const string ProviderId = "claude-code";

    /// <summary>Default launch script when the user hasn't customized it.
    /// 'Auto Mode' (set and remembered in Claude) is Claude Code's equivalent
    /// of GitHub Copilot's <c>--allow-all-tools</c>: it suppresses the
    /// per-tool permission prompts, so no flag is needed here.</summary>
    public const string ClaudeDefaultLaunchCommands = "claude";

    public string Id => ProviderId;
    public string DisplayName => "Claude Code";
    public string DefaultLaunchCommands => ClaudeDefaultLaunchCommands;

    /// <summary>
    /// Builds the command ai-frame runs in the agent terminal.
    /// Non-empty / non-<c>#</c>-comment lines from <paramref name="commands"/>
    /// are chained with <c>&amp;&amp;</c> in order; the last line is the
    /// persistent interactive Claude process.
    /// <para>
    /// When <paramref name="initialPromptFile"/> is supplied and exists, the
    /// final line is wrapped in a PowerShell shim that reads the prompt and
    /// passes it as Claude's positional initial-message argument, deleting
    /// the file afterwards so each prompt is consumed exactly once. Earlier
    /// lines run unmodified.
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
            $"{finalCmd} $p";

        return preamble + $"pwsh -NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"";
    }

    public IAgentSessionStore CreateSessionStore() => new ClaudeCodeSessionMonitor();
}

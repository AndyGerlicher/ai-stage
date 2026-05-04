using System.IO;

namespace AgentSessions.Providers.ClaudeCode.Internal;

/// <summary>
/// Locates the per-user Claude Code session-state directories.
/// </summary>
internal static class ClaudePaths
{
    /// <summary>
    /// %USERPROFILE%\.claude\sessions — one &lt;pid&gt;.json lock file per
    /// running interactive Claude Code process.
    /// </summary>
    public static string SessionsRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "sessions");
}

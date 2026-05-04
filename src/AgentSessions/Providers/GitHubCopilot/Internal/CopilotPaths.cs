using System.IO;

namespace AgentSessions.Providers.GitHubCopilot.Internal;

/// <summary>
/// Locates the per-user Copilot CLI session-state directory.
/// </summary>
internal static class CopilotPaths
{
    /// <summary>
    /// %USERPROFILE%\.copilot\session-state — the parent of every session folder.
    /// </summary>
    public static string SessionStateRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot",
        "session-state");
}

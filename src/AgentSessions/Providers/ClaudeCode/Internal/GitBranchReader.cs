using System.IO;

namespace AgentSessions.Providers.ClaudeCode.Internal;

/// <summary>
/// Reads the current branch and git root for a working directory by walking
/// <c>.git/HEAD</c> directly. Worktree-aware: when <c>.git</c> is a file
/// containing <c>gitdir: ...</c>, the gitdir's HEAD is read instead.
/// Avoids spawning <c>git</c> on every poll.
/// </summary>
internal static class GitBranchReader
{
    public sealed record GitInfo(string? Branch, string? GitRoot);

    public static GitInfo Read(string cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return new GitInfo(null, null);

        try
        {
            string gitPath = Path.Combine(cwd, ".git");

            string headPath;
            string? gitRoot;

            if (File.Exists(gitPath))
            {
                // Linked worktree: .git is a file with "gitdir: <path>".
                string gitdirLine = File.ReadAllText(gitPath).Trim();
                const string gitdirPrefix = "gitdir: ";
                if (!gitdirLine.StartsWith(gitdirPrefix, StringComparison.Ordinal))
                    return new GitInfo(null, null);

                string gitdir = gitdirLine[gitdirPrefix.Length..];
                if (!Path.IsPathRooted(gitdir))
                    gitdir = Path.GetFullPath(Path.Combine(cwd, gitdir));

                headPath = Path.Combine(gitdir, "HEAD");
                gitRoot = cwd;
            }
            else if (Directory.Exists(gitPath))
            {
                headPath = Path.Combine(gitPath, "HEAD");
                gitRoot = cwd;
            }
            else
            {
                return new GitInfo(null, null);
            }

            if (!File.Exists(headPath))
                return new GitInfo(null, gitRoot);

            string content = File.ReadAllText(headPath).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (content.StartsWith(refPrefix, StringComparison.Ordinal))
                return new GitInfo(content[refPrefix.Length..], gitRoot);

            // Detached HEAD — short hash.
            string detached = content.Length >= 7 ? content[..7] : content;
            return new GitInfo(detached, gitRoot);
        }
        catch (IOException) { return new GitInfo(null, null); }
        catch (UnauthorizedAccessException) { return new GitInfo(null, null); }
    }
}

using System.IO;

namespace AiStage.Services;

/// <summary>
/// Pure file-based git metadata readers. Must never throw — returns null/fallbacks on any failure.
/// Avoids spawning git.exe for speed and concurrency.
/// </summary>
internal static class GitMetadata
{
    /// <summary>
    /// Returns true if the directory is a main git repository (contains a .git directory).
    /// Returns false for linked worktrees (where .git is a file) or non-repos.
    /// </summary>
    public static bool IsMainRepo(string directoryPath)
    {
        try
        {
            string gitPath = Path.Combine(directoryPath, ".git");
            return Directory.Exists(gitPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the branch name from a HEAD file. Returns the branch name, or a short SHA
    /// for detached HEAD, or "(unknown)" on failure.
    /// </summary>
    public static string ReadBranch(string headFilePath)
    {
        try
        {
            if (!File.Exists(headFilePath))
                return "(unknown)";

            string content = File.ReadAllText(headFilePath).Trim();

            const string refPrefix = "ref: refs/heads/";
            if (content.StartsWith(refPrefix, StringComparison.Ordinal))
                return content[refPrefix.Length..];

            // Detached HEAD — content is a full SHA
            if (content.Length >= 7)
                return content[..7];

            return "(unknown)";
        }
        catch
        {
            return "(unknown)";
        }
    }

    /// <summary>
    /// Returns the most recent mtime among candidate paths, or null if none exist.
    /// Used as the "last activity" signal (logs/HEAD preferred, falling back to HEAD, then directory).
    /// </summary>
    public static DateTime? LatestMTime(params string[] candidatePaths)
    {
        DateTime? best = null;
        foreach (string p in candidatePaths)
        {
            try
            {
                if (File.Exists(p))
                {
                    var t = File.GetLastWriteTimeUtc(p);
                    if (best is null || t > best) best = t;
                }
                else if (Directory.Exists(p))
                {
                    var t = Directory.GetLastWriteTimeUtc(p);
                    if (best is null || t > best) best = t;
                }
            }
            catch
            {
                // ignore — stat may fail on locked files
            }
        }
        return best;
    }

    /// <summary>
    /// Returns the branch and last-activity timestamp for a main repo at <paramref name="repoPath"/>.
    /// </summary>
    public static (string branch, DateTime? lastActivity) ReadMainRepoMetadata(string repoPath)
    {
        string gitDir = Path.Combine(repoPath, ".git");
        string branch = ReadBranch(Path.Combine(gitDir, "HEAD"));
        DateTime? activity = LatestMTime(
            Path.Combine(gitDir, "logs", "HEAD"),
            Path.Combine(gitDir, "HEAD"),
            gitDir);
        return (branch, activity);
    }

    /// <summary>
    /// Enumerates linked worktrees for a main repo by reading .git/worktrees/*/gitdir entries.
    /// For each worktree, reads its per-worktree HEAD and logs/HEAD (stored under .git/worktrees/&lt;name&gt;/).
    /// </summary>
    public static IEnumerable<(string name, string path, string branch, DateTime? lastActivity)>
        EnumerateWorktrees(string repoPath)
    {
        string worktreesDir = Path.Combine(repoPath, ".git", "worktrees");
        if (!Directory.Exists(worktreesDir))
            yield break;

        string[] entries;
        try
        {
            entries = Directory.GetDirectories(worktreesDir);
        }
        catch
        {
            yield break;
        }

        foreach (string entry in entries)
        {
            string gitdirFile = Path.Combine(entry, "gitdir");
            string? workingDir = null;

            try
            {
                if (!File.Exists(gitdirFile))
                    continue;

                // gitdir contents: absolute path to the worktree's .git file
                string gitFile = File.ReadAllText(gitdirFile).Trim();
                workingDir = Path.GetDirectoryName(gitFile);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(workingDir))
                continue;

            // Skip if the worktree directory no longer exists (stale entry)
            if (!Directory.Exists(workingDir))
                continue;

            string branch = ReadBranch(Path.Combine(entry, "HEAD"));
            DateTime? activity = LatestMTime(
                Path.Combine(entry, "logs", "HEAD"),
                Path.Combine(entry, "HEAD"),
                entry);

            yield return (
                name: Path.GetFileName(entry),
                path: workingDir,
                branch,
                lastActivity: activity);
        }
    }
}

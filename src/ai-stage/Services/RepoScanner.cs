using System.IO;
using AiStage.Models;

namespace AiStage.Services;

/// <summary>
/// Concurrently scans a root directory for git repositories (one level deep)
/// and reads their metadata plus any linked worktrees.
/// </summary>
internal static class RepoScanner
{
    public static async Task<IReadOnlyList<RepoNode>> ScanAsync(string rootPath, string branchPrefix, CancellationToken ct = default)
    {
        if (!Directory.Exists(rootPath))
            return [];

        string[] subdirs;
        try
        {
            subdirs = Directory.GetDirectories(rootPath);
        }
        catch
        {
            return [];
        }

        var results = new System.Collections.Concurrent.ConcurrentBag<RepoNode>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(subdirs, parallelOptions, (dir, innerCt) =>
        {
            try
            {
                if (!GitMetadata.IsMainRepo(dir))
                    return ValueTask.CompletedTask;

                var (branch, activity) = GitMetadata.ReadMainRepoMetadata(dir);

                var node = new RepoNode
                {
                    Name = Path.GetFileName(dir),
                    Path = dir,
                    Branch = branch,
                    LastActivityUtc = activity,
                };

                foreach (var wt in GitMetadata.EnumerateWorktrees(dir))
                {
                    node.Worktrees.Add(new WorktreeNode
                    {
                        Name = wt.name,
                        Path = wt.path,
                        Branch = wt.branch,
                        LastActivityUtc = wt.lastActivity,
                        ParentRepoPath = dir,
                        BranchPrefix = branchPrefix,
                    });
                }

                results.Add(node);
            }
            catch
            {
                // Swallow per-repo failures so one bad entry doesn't abort the scan.
            }
            return ValueTask.CompletedTask;
        });

        return results
            .OrderByDescending(r => r.LastActivityUtc ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

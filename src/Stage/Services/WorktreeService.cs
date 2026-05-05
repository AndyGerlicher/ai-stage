using System.Diagnostics;
using System.IO;

namespace Stage.Services;

internal sealed record WorktreeResult(bool Success, string? Path, string? Error);

/// <summary>
/// Creates and resets git worktrees via the git CLI. Worktrees are placed at
/// &lt;repoParent&gt;\&lt;RepoName&gt;.wt\&lt;slot&gt; using auto-numbered slots.
/// </summary>
internal static class WorktreeService
{
    /// <summary>
    /// Creates a new worktree in the next available numbered slot, branching from origin/main.
    /// </summary>
    public static async Task<WorktreeResult> CreateAsync(string mainRepoPath, string branchPrefix, string branchSuffix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(branchSuffix))
            return new WorktreeResult(false, null, "Branch name cannot be empty.");

        string? parent = Path.GetDirectoryName(mainRepoPath);
        if (string.IsNullOrEmpty(parent))
            return new WorktreeResult(false, null, "Could not determine parent directory of repo.");

        string repoName = Path.GetFileName(mainRepoPath);
        string worktreeRoot = Path.Combine(parent, $"{repoName}.wt");

        try
        {
            Directory.CreateDirectory(worktreeRoot);
        }
        catch (Exception ex)
        {
            return new WorktreeResult(false, null, $"Could not create worktree root folder: {ex.Message}");
        }

        int slot = NextSlot(worktreeRoot);
        string targetPath = Path.Combine(worktreeRoot, slot.ToString());

        // Fetch latest before creating the worktree.
        await RunGitAsync(mainRepoPath, ["fetch"], ct);

        string fullBranch = branchPrefix + branchSuffix;

        // First attempt: create a new branch starting at origin/main.
        var (ok, stderr) = await RunGitAsync(mainRepoPath, ["worktree", "add", "-b", fullBranch, targetPath, "origin/main"], ct);
        if (ok)
            return new WorktreeResult(true, targetPath, null);

        // Retry without -b: the branch may already exist.
        var (ok2, stderr2) = await RunGitAsync(mainRepoPath, ["worktree", "add", targetPath, fullBranch], ct);
        if (ok2)
        {
            await RunGitAsync(targetPath, ["reset", "--hard", "origin/main"], ct);
            return new WorktreeResult(true, targetPath, null);
        }

        string err = string.IsNullOrWhiteSpace(stderr) ? stderr2 : stderr;
        return new WorktreeResult(false, null, err);
    }

    /// <summary>
    /// Resets an existing worktree to origin/main on a new (or existing) branch.
    /// </summary>
    public static async Task<WorktreeResult> ResetAsync(string mainRepoPath, string worktreePath, string branchPrefix, string branchSuffix, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(branchSuffix))
            return new WorktreeResult(false, null, "Branch name cannot be empty.");

        await RunGitAsync(mainRepoPath, ["fetch"], ct);

        string fullBranch = branchPrefix + branchSuffix;

        // Switch to new (or reset existing) branch at origin/main.
        var (ok, stderr) = await RunGitAsync(worktreePath, ["checkout", "-B", fullBranch, "origin/main"], ct);
        if (!ok)
            return new WorktreeResult(false, null, $"Branch switch failed: {stderr}");

        // Clean untracked files and directories.
        await RunGitAsync(worktreePath, ["clean", "-fdx"], ct);

        return new WorktreeResult(true, worktreePath, null);
    }

    /// <summary>Returns the next available integer slot in the worktree root.</summary>
    public static int NextSlot(string worktreeRoot)
    {
        if (!Directory.Exists(worktreeRoot))
            return 1;

        var existing = new HashSet<int>();
        try
        {
            foreach (string dir in Directory.GetDirectories(worktreeRoot))
            {
                if (int.TryParse(Path.GetFileName(dir), out int n))
                    existing.Add(n);
            }
        }
        catch { /* directory read failure — start at 1 */ }

        int slot = 1;
        while (existing.Contains(slot)) slot++;
        return slot;
    }

    /// <summary>
    /// Removes a worktree via <c>git worktree remove --force</c>. Force is used
    /// because the caller confirmed they want the folder wiped.
    /// </summary>
    public static async Task<WorktreeResult> DeleteAsync(string mainRepoPath, string worktreePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(worktreePath))
            return new WorktreeResult(false, null, "Worktree path is empty.");

        var (ok, stderr) = await RunGitAsync(mainRepoPath, ["worktree", "remove", "--force", worktreePath], ct);
        if (ok)
            return new WorktreeResult(true, worktreePath, null);

        // If git refused but the directory still exists, try a manual cleanup as a last resort:
        // delete the directory and prune worktree admin entries.
        try
        {
            if (Directory.Exists(worktreePath))
                Directory.Delete(worktreePath, recursive: true);
            await RunGitAsync(mainRepoPath, ["worktree", "prune"], ct);
            return new WorktreeResult(true, worktreePath, null);
        }
        catch (Exception ex)
        {
            return new WorktreeResult(false, null, $"{stderr}\n\nFallback cleanup also failed: {ex.Message}".Trim());
        }
    }

    /// <summary>
    /// Returns the output of <c>git status --porcelain</c> for the given worktree.
    /// Empty string means the worktree is clean.
    /// </summary>
    public static async Task<string> GetStatusAsync(string worktreePath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = worktreePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--porcelain");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return "(could not run git status)";

            string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return stdout.Trim();
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }

    private static async Task<(bool ok, string stderr)> RunGitAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return (false, "Failed to start git.");

            string stderr = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode == 0, stderr.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

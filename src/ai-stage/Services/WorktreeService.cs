using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AiStage.Services;

internal sealed record WorktreeResult(bool Success, string? Path, string? Error);

/// <summary>
/// Creates and resets git worktrees via the git CLI. Worktrees are placed at
/// &lt;repoParent&gt;\&lt;RepoName&gt;.wt\&lt;slot&gt; using auto-numbered slots.
/// </summary>
internal static class WorktreeService
{
    /// <summary>
    /// Creates a new worktree in the next available numbered slot, branching from
    /// <c>origin/&lt;defaultBranch&gt;</c>.
    /// </summary>
    public static async Task<WorktreeResult> CreateAsync(string mainRepoPath, string branchPrefix, string branchSuffix, string defaultBranch, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(branchSuffix))
            return new WorktreeResult(false, null, "Branch name cannot be empty.");
        if (string.IsNullOrWhiteSpace(defaultBranch))
            defaultBranch = StageConfig.DefaultBranchFallback;

        WorktreeResult? prep = PrepareTargetPath(mainRepoPath, out string targetPath);
        if (prep is not null)
            return prep;

        // Fetch latest before creating the worktree.
        await RunGitAsync(mainRepoPath, ["fetch"], ct);

        string fullBranch = branchPrefix + branchSuffix;
        string targetRef = $"origin/{defaultBranch}";

        // First attempt: create a new branch starting at origin/<defaultBranch>.
        var (ok, stderr) = await RunGitAsync(mainRepoPath, ["worktree", "add", "-b", fullBranch, targetPath, targetRef], ct);
        if (ok)
            return new WorktreeResult(true, targetPath, null);

        // Retry without -b: the branch may already exist.
        var (ok2, stderr2) = await RunGitAsync(mainRepoPath, ["worktree", "add", targetPath, fullBranch], ct);
        if (ok2)
        {
            await RunGitAsync(targetPath, ["reset", "--hard", targetRef], ct);
            return new WorktreeResult(true, targetPath, null);
        }

        string err = string.IsNullOrWhiteSpace(stderr) ? stderr2 : stderr;
        return new WorktreeResult(false, null, err);
    }

    /// <summary>
    /// Creates a new worktree in the next available numbered slot that lands on
    /// an existing ref instead of a freshly-created branch. Backs the
    /// "Existing branch" and "Default branch" options of the create dialog.
    /// <para>
    /// When <paramref name="branchName"/> is supplied the worktree checks out a
    /// local branch of that name, creating it from <paramref name="targetRef"/>
    /// if it doesn't exist yet. If the branch already exists and is checked out
    /// in another worktree, git refuses the checkout, so we fall back to a
    /// detached checkout of <paramref name="targetRef"/>. When
    /// <paramref name="branchName"/> is <see langword="null"/> (the
    /// "Default branch" flow) the worktree is created detached directly,
    /// because the default branch is normally already checked out in the
    /// primary worktree.
    /// </para>
    /// </summary>
    public static async Task<WorktreeResult> CreateFromRefAsync(string mainRepoPath, string targetRef, string? branchName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetRef))
            return new WorktreeResult(false, null, "Target ref cannot be empty.");

        WorktreeResult? prep = PrepareTargetPath(mainRepoPath, out string targetPath);
        if (prep is not null)
            return prep;

        // Fetch so origin/* refs are current before we branch off them.
        await RunGitAsync(mainRepoPath, ["fetch", "--prune"], ct);

        if (!string.IsNullOrWhiteSpace(branchName))
        {
            // 1) Create a new local branch tracking the remote ref.
            var (ok, stderr) = await RunGitAsync(mainRepoPath, ["worktree", "add", "-b", branchName, targetPath, targetRef], ct);
            if (ok)
                return new WorktreeResult(true, targetPath, null);

            // 2) The local branch already exists — check it out as-is.
            var (ok2, _) = await RunGitAsync(mainRepoPath, ["worktree", "add", targetPath, branchName], ct);
            if (ok2)
                return new WorktreeResult(true, targetPath, null);

            // 3) Branch is checked out in another worktree (or some other
            //    failure) — fall back to a detached checkout so the user still
            //    lands on the branch's commit.
            var (ok3, stderr3) = await RunGitAsync(mainRepoPath, ["worktree", "add", "--detach", targetPath, targetRef], ct);
            if (ok3)
                return new WorktreeResult(true, targetPath, null);

            return new WorktreeResult(false, null, string.IsNullOrWhiteSpace(stderr3) ? stderr : stderr3);
        }

        // Default-branch flow: detached checkout (the branch is usually already
        // checked out in the primary worktree).
        var (okd, stderrd) = await RunGitAsync(mainRepoPath, ["worktree", "add", "--detach", targetPath, targetRef], ct);
        return okd
            ? new WorktreeResult(true, targetPath, null)
            : new WorktreeResult(false, null, stderrd);
    }

    /// <summary>
    /// Resolves the next worktree slot under <c>&lt;repoParent&gt;\&lt;RepoName&gt;.wt</c>,
    /// ensuring the root folder exists. Returns a failed <see cref="WorktreeResult"/>
    /// on error (and leaves <paramref name="targetPath"/> empty), or
    /// <see langword="null"/> on success with the slot path in
    /// <paramref name="targetPath"/>.
    /// </summary>
    private static WorktreeResult? PrepareTargetPath(string mainRepoPath, out string targetPath)
    {
        targetPath = "";
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
        targetPath = Path.Combine(worktreeRoot, slot.ToString());
        return null;
    }

    /// <summary>
    /// Resets an existing worktree by running the supplied
    /// <paramref name="resetCommands"/> (one command per line, executed via
    /// <c>cmd.exe /c</c> in the worktree's working directory). Tokens
    /// substituted in each command line:
    /// <list type="bullet">
    /// <item><c>&lt;new-branch&gt;</c> → <paramref name="newBranch"/> (the
    /// local branch name to land on; may be empty for modes that do a
    /// detached checkout).</item>
    /// <item><c>&lt;target-ref&gt;</c> → <paramref name="targetRef"/> (the
    /// git ref to reset onto, e.g. <c>origin/main</c>).</item>
    /// </list>
    /// Aborts on the first non-zero exit and returns the failing line's stderr.
    /// </summary>
    public static async Task<WorktreeResult> ResetAsync(
        string mainRepoPath,
        string worktreePath,
        string newBranch,
        string targetRef,
        string resetCommands,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetRef))
            return new WorktreeResult(false, null, "Target ref cannot be empty.");
        if (string.IsNullOrWhiteSpace(resetCommands))
            return new WorktreeResult(false, null, "Reset commands are empty (configure them in Settings).");

        // Run each non-empty / non-comment line through cmd.exe /c in the
        // worktree dir. Lines starting with '#' are ignored so users can
        // annotate their command list.
        string[] lines = resetCommands.Replace("\r\n", "\n").Split('\n');
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            string command = line
                .Replace("<new-branch>", newBranch, StringComparison.Ordinal)
                .Replace("<target-ref>", targetRef, StringComparison.Ordinal);
            var (ok, stderr) = await RunShellAsync(worktreePath, command, ct);
            if (!ok)
                return new WorktreeResult(false, null, $"`{command}` failed:\n{stderr}");
        }

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

    /// <summary>
    /// Resets <paramref name="worktreePath"/> onto <paramref name="targetRef"/>
    /// by invoking <c>git</c> directly (no shell). Used by ai-stage's
    /// "Existing branch" and "Default branch" reset modes, where the target
    /// ref comes from user-pickable data (a remote branch name from
    /// <see cref="ListRemoteBranchesAsync"/> or an editable ComboBox) and
    /// must therefore not be interpolated into a shell command line.
    /// <para>
    /// When <paramref name="branchName"/> is supplied the checkout step uses
    /// <c>git checkout &lt;branchName&gt;</c>, which creates or switches to a
    /// local tracking branch (the "Existing branch" flow). When it is
    /// <see langword="null"/> the checkout uses <c>--force --detach</c>
    /// instead (the "Default branch" flow, where the branch is typically
    /// already checked out in the primary worktree).
    /// </para>
    /// Sequence:
    /// <list type="number">
    /// <item><c>git fetch --prune</c> in the main repo.</item>
    /// <item><c>git clean -fdx</c> in the worktree.</item>
    /// <item><c>git checkout &lt;branchName&gt;</c> <em>or</em>
    /// <c>git checkout --force --detach &lt;targetRef&gt;</c>.</item>
    /// <item><c>git reset --hard &lt;targetRef&gt;</c>.</item>
    /// <item><c>git clean -fdx</c> again.</item>
    /// </list>
    /// Returns the first failing step's stderr on error.
    /// </summary>
    public static async Task<WorktreeResult> ResetToRefAsync(
        string mainRepoPath,
        string worktreePath,
        string targetRef,
        string? branchName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetRef))
            return new WorktreeResult(false, null, "Target ref cannot be empty.");

        // Fetch in the main repo so origin/* refs are current.
        var (ok, stderr) = await RunGitAsync(mainRepoPath, ["fetch", "--prune"], ct);
        // Tolerate fetch failure (offline mode); we'll surface a clearer error
        // on checkout if the ref genuinely doesn't exist.

        (ok, stderr) = await RunGitAsync(worktreePath, ["clean", "-fdx"], ct);
        if (!ok) return new WorktreeResult(false, null, $"git clean failed:\n{stderr}");

        if (!string.IsNullOrWhiteSpace(branchName))
        {
            // Local tracking-branch checkout (Existing Branch mode).
            (ok, stderr) = await RunGitAsync(worktreePath, ["checkout", branchName], ct);
            if (!ok) return new WorktreeResult(false, null, $"git checkout {branchName} failed:\n{stderr}");
        }
        else
        {
            // Detached checkout (Default Branch mode — the branch is usually
            // already checked out in the primary worktree).
            (ok, stderr) = await RunGitAsync(worktreePath, ["checkout", "--force", "--detach", targetRef], ct);
            if (!ok) return new WorktreeResult(false, null, $"git checkout {targetRef} failed:\n{stderr}");
        }

        (ok, stderr) = await RunGitAsync(worktreePath, ["reset", "--hard", targetRef], ct);
        if (!ok) return new WorktreeResult(false, null, $"git reset --hard {targetRef} failed:\n{stderr}");

        (ok, stderr) = await RunGitAsync(worktreePath, ["clean", "-fdx"], ct);
        if (!ok) return new WorktreeResult(false, null, $"git clean failed:\n{stderr}");

        return new WorktreeResult(true, worktreePath, null);
    }

    /// <summary>
    /// Fetches from origin then returns the list of remote branch names
    /// (with the <c>origin/</c> prefix stripped) sorted alphabetically.
    /// Excludes the <c>HEAD</c> pseudo-ref. Returns an empty list on
    /// failure (callers should treat this as "nothing to pick from").
    /// </summary>
    public static async Task<IReadOnlyList<string>> ListRemoteBranchesAsync(string mainRepoPath, CancellationToken ct = default)
    {
        // Best-effort fetch; ignore failures and try to list whatever we
        // already have cached so the dialog is still usable offline.
        await RunGitAsync(mainRepoPath, ["fetch", "--prune"], ct);

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = mainRepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("for-each-ref");
        psi.ArgumentList.Add("--format=%(refname:strip=3)");
        psi.ArgumentList.Add("refs/remotes/origin");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return Array.Empty<string>();

            string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0) return Array.Empty<string>();

            var branches = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in stdout.Replace("\r\n", "\n").Split('\n'))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                if (string.Equals(name, "HEAD", StringComparison.OrdinalIgnoreCase)) continue;
                branches.Add(name);
            }
            return branches.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
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

    /// <summary>
    /// Runs a single user-supplied command line through <c>cmd.exe /c</c> in
    /// <paramref name="workingDir"/>. Returns success + stderr like
    /// <see cref="RunGitAsync"/> so callers can chain with the same shape.
    /// Going through cmd.exe lets the user use shell features like <c>&amp;&amp;</c>,
    /// pipes, and redirection in their reset commands.
    /// </summary>
    private static async Task<(bool ok, string stderr)> RunShellAsync(string workingDir, string commandLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDir,
            // /d disables AutoRun keys, /s preserves the quoted command for /c.
            Arguments = $"/d /s /c \"{commandLine}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return (false, "Failed to start cmd.exe.");

            // Drain both streams concurrently; large outputs (e.g. a noisy
            // git fetch) can otherwise deadlock when only stderr is read.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (proc.ExitCode == 0) return (true, "");
            // Many git commands write the meaningful error to stdout; surface
            // whichever stream has content.
            string err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (false, err.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

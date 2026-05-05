using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AgentSessions;
using AiStage.Models;

namespace AiStage.Services;

/// <summary>
/// Subscribes to an <see cref="IAgentSessionStore"/> and pushes the live
/// session state onto ai-stage's repo and worktree node models. Marshals to the
/// UI thread.
/// </summary>
internal sealed class AgentSessionRowBinder : IDisposable
{
    private readonly IAgentSessionStore _store;
    private readonly ObservableCollection<RepoNode> _repos;
    private readonly Dispatcher _dispatcher;

    private IReadOnlyList<AgentSession> _lastSnapshot = Array.Empty<AgentSession>();

    public AgentSessionRowBinder(
        IAgentSessionStore store,
        ObservableCollection<RepoNode> repos,
        Dispatcher dispatcher)
    {
        _store = store;
        _repos = repos;
        _dispatcher = dispatcher;

        _store.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot(_store.CurrentSessions); // first paint with whatever is cached
    }

    public void Dispose()
    {
        _store.SnapshotChanged -= OnSnapshotChanged;
    }

    /// <summary>
    /// Reapplies the last snapshot — call after the repo list is refreshed so
    /// freshly created node instances pick up their session state.
    /// </summary>
    public void ReapplyToCurrentRepos() => ApplySnapshot(_lastSnapshot);

    /// <summary>
    /// Returns the most recently updated session whose cwd matches <paramref name="path"/>,
    /// or null if no live session matches.
    /// </summary>
    public AgentSession? FindSessionForPath(string path)
    {
        string normalized = NormalizePath(path);
        AgentSession? best = null;
        foreach (var s in _lastSnapshot)
        {
            if (!string.Equals(NormalizePath(s.Cwd), normalized, StringComparison.OrdinalIgnoreCase))
                continue;
            if (best is null || s.LastUpdatedUtc > best.LastUpdatedUtc)
                best = s;
        }
        return best;
    }

    private void OnSnapshotChanged(IReadOnlyList<AgentSession> snapshot)
    {
        if (_dispatcher.CheckAccess())
            ApplySnapshot(snapshot);
        else
            _dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
    }

    private void ApplySnapshot(IReadOnlyList<AgentSession> snapshot)
    {
        _lastSnapshot = snapshot;

        // Bucket sessions by normalized cwd.
        var byCwd = new Dictionary<string, (AgentSessionStatus status, int count)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var s in snapshot)
        {
            string key = NormalizePath(s.Cwd);
            if (!byCwd.TryGetValue(key, out var agg))
            {
                byCwd[key] = (s.Status, 1);
            }
            else
            {
                // Worst-state-wins: any Processing flips the row to Processing.
                var status = (agg.status == AgentSessionStatus.Processing
                              || s.Status == AgentSessionStatus.Processing)
                    ? AgentSessionStatus.Processing
                    : AgentSessionStatus.Idle;
                byCwd[key] = (status, agg.count + 1);
            }
        }

        foreach (var repo in _repos)
        {
            ApplyToRepo(repo, byCwd);
            foreach (var wt in repo.Worktrees)
                ApplyToWorktree(wt, byCwd);
        }
    }

    private static void ApplyToRepo(
        RepoNode repo,
        Dictionary<string, (AgentSessionStatus status, int count)> byCwd)
    {
        if (byCwd.TryGetValue(NormalizePath(repo.Path), out var agg))
        {
            repo.AgentState = agg.status;
            repo.AgentSessionCount = agg.count;
        }
        else
        {
            repo.AgentState = null;
            repo.AgentSessionCount = 0;
        }
    }

    private static void ApplyToWorktree(
        WorktreeNode wt,
        Dictionary<string, (AgentSessionStatus status, int count)> byCwd)
    {
        if (byCwd.TryGetValue(NormalizePath(wt.Path), out var agg))
        {
            wt.AgentState = agg.status;
            wt.AgentSessionCount = agg.count;
        }
        else
        {
            wt.AgentState = null;
            wt.AgentSessionCount = 0;
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            string full = Path.GetFullPath(path);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

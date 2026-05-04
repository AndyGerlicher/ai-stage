using System.Diagnostics;
using System.IO;
using AgentSessions.Providers.ClaudeCode.Internal;

namespace AgentSessions.Providers.ClaudeCode;

/// <summary>
/// Watches the Claude Code per-process lock files at
/// <c>~/.claude/sessions/&lt;pid&gt;.json</c> and exposes a live snapshot of
/// running sessions. Hosts subscribe to <see cref="SnapshotChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// Detection: each running interactive Claude Code process maintains exactly
/// one <c>&lt;pid&gt;.json</c> file in <c>~/.claude/sessions</c>. We verify
/// the PID against the live process list (and capture its start time on first
/// sighting) to skip stale lock files left by crashed processes and to defend
/// against PID recycling.
/// </para>
/// <para>
/// State: <see cref="AgentSessionStatus.Processing"/> when the lock file's
/// <c>status</c> field is <c>"busy"</c>; otherwise
/// <see cref="AgentSessionStatus.Idle"/>. The lock file is re-read whenever
/// its <see cref="File.GetLastWriteTimeUtc(string)"/> changes.
/// </para>
/// </remarks>
public sealed class ClaudeCodeSessionMonitor : IAgentSessionStore
{
    private readonly string _sessionsRoot;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly Dictionary<int, SessionTracker> _trackers = new();

    private volatile IReadOnlyList<AgentSession> _snapshot = Array.Empty<AgentSession>();
    private int _started; // 0 = not started, 1 = started
    private Task? _loop;

    public event Action<IReadOnlyList<AgentSession>>? SnapshotChanged;

    public IReadOnlyList<AgentSession> CurrentSessions => _snapshot;

    public ClaudeCodeSessionMonitor()
        : this(ClaudePaths.SessionsRoot, TimeSpan.FromMilliseconds(750)) { }

    public ClaudeCodeSessionMonitor(string sessionsRoot, TimeSpan pollInterval)
    {
        _sessionsRoot = sessionsRoot;
        _pollInterval = pollInterval;
    }

    /// <summary>Starts the background polling loop. Idempotent and thread-safe.</summary>
    public void Start()
    {
        if (System.Threading.Interlocked.Exchange(ref _started, 1) == 1)
            return;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* already disposed */ }
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* fall through */ }
        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Initial pass before the timer ticks so the first snapshot arrives quickly.
        TickSafe();

        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                TickSafe();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void TickSafe()
    {
        try { Tick(); }
        catch
        {
            // The loop must never die — if a poll fails (e.g. transient I/O)
            // we just wait for the next tick.
        }
    }

    private void Tick()
    {
        if (!Directory.Exists(_sessionsRoot))
        {
            ReplaceSnapshotIfChanged(Array.Empty<AgentSession>());
            return;
        }

        var seen = new HashSet<int>();
        var sessions = new List<AgentSession>();
        bool changed = false;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_sessionsRoot, "*.json");
        }
        catch
        {
            return;
        }

        foreach (var path in files)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(stem, out int pid)) continue;

            DateTime? startTimeUtc = TryGetProcessStartTimeUtc(pid);
            if (startTimeUtc is null) continue; // process not alive or inaccessible

            seen.Add(pid);

            SessionTracker tracker;
            lock (_gate)
            {
                if (!_trackers.TryGetValue(pid, out tracker!))
                {
                    tracker = new SessionTracker(pid, path, startTimeUtc.Value);
                    _trackers[pid] = tracker;
                    changed = true;
                }
                else if (tracker.ProcessStartTimeUtc != startTimeUtc.Value)
                {
                    // PID was recycled (different process now occupies the same id).
                    tracker.ResetForNewProcess(startTimeUtc.Value);
                    changed = true;
                }
            }

            if (tracker.Refresh())
                changed = true;

            if (tracker.Snapshot is { } s)
                sessions.Add(s);
        }

        // Remove trackers whose lock file disappeared or whose process exited.
        lock (_gate)
        {
            var toRemove = _trackers.Keys.Where(k => !seen.Contains(k)).ToList();
            foreach (var k in toRemove)
            {
                _trackers.Remove(k);
                changed = true;
            }
        }

        if (changed)
        {
            // Force-publish: trackers reported real changes (status, fields,
            // add/remove). Skip the equality fallback so we never swallow a
            // legitimate update on Branch/Repository/Summary/etc.
            PublishSnapshot(sessions);
        }
        else
        {
            ReplaceSnapshotIfChanged(sessions);
        }
    }

    private void PublishSnapshot(IReadOnlyList<AgentSession> next)
    {
        _snapshot = next;
        try { SnapshotChanged?.Invoke(next); } catch { /* swallow handler exceptions */ }
    }

    private void ReplaceSnapshotIfChanged(IReadOnlyList<AgentSession> next)
    {
        if (SnapshotEquals(_snapshot, next))
            return;
        PublishSnapshot(next);
    }

    private static bool SnapshotEquals(IReadOnlyList<AgentSession> a, IReadOnlyList<AgentSession> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            // Ordering can drift; compare on the full record.
            var ai = a[i];
            bool found = false;
            for (int j = 0; j < b.Count; j++)
            {
                if (ai.Equals(b[j])) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    /// <summary>
    /// Returns the process start time for <paramref name="pid"/> in UTC, or
    /// null if the process isn't alive or its identity cannot be established.
    /// </summary>
    private static DateTime? TryGetProcessStartTimeUtc(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return null;
            return p.StartTime.ToUniversalTime();
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (System.ComponentModel.Win32Exception)
        {
            // Insufficient permissions to read StartTime; treat as unknown so
            // we don't accept a possibly-recycled PID.
            return null;
        }
    }

    /// <summary>
    /// Per-session state held by the monitor. Knows how to re-read its lock
    /// file and produce an immutable <see cref="AgentSession"/> snapshot.
    /// </summary>
    private sealed class SessionTracker
    {
        private readonly int _pid;
        private readonly string _lockPath;

        private long _lastLockLwt = -1;
        private SessionLockReader.SessionLockInfo? _lock;
        private GitBranchReader.GitInfo? _git;
        private string? _gitCwd; // cwd that _git was computed against
        private AgentSessionStatus? _lastStatus;

        public DateTime ProcessStartTimeUtc { get; private set; }
        public AgentSession? Snapshot { get; private set; }

        public SessionTracker(int pid, string lockPath, DateTime startUtc)
        {
            _pid = pid;
            _lockPath = lockPath;
            ProcessStartTimeUtc = startUtc;
        }

        /// <summary>
        /// Resets all state when a recycled PID is observed. The same lock
        /// path may be re-bound to a fresh process instance.
        /// </summary>
        public void ResetForNewProcess(DateTime startUtc)
        {
            ProcessStartTimeUtc = startUtc;
            _lastLockLwt = -1;
            _lock = null;
            _git = null;
            _gitCwd = null;
            _lastStatus = null;
            Snapshot = null;
        }

        /// <summary>
        /// Re-reads the lock file if it changed and rebuilds the snapshot.
        /// Returns true when the snapshot needs to be re-emitted.
        /// </summary>
        public bool Refresh()
        {
            bool lockChanged = RefreshLock();

            if (_lock is null)
            {
                bool wasNonNull = Snapshot is not null;
                Snapshot = null;
                return wasNonNull;
            }

            // Refresh git info when cwd changes (rare — usually once per session).
            if (_git is null || !string.Equals(_gitCwd, _lock.Cwd, StringComparison.OrdinalIgnoreCase))
            {
                _git = GitBranchReader.Read(_lock.Cwd);
                _gitCwd = _lock.Cwd;
                lockChanged = true;
            }

            var status = string.Equals(_lock.Status, "busy", StringComparison.OrdinalIgnoreCase)
                ? AgentSessionStatus.Processing
                : string.Equals(_lock.Status, "waiting", StringComparison.OrdinalIgnoreCase)
                    ? AgentSessionStatus.WaitingForInput
                    : AgentSessionStatus.Idle;

            bool changed = lockChanged;
            if (status != _lastStatus)
            {
                _lastStatus = status;
                changed = true;
            }

            if (changed || Snapshot is null)
            {
                string repository = _git?.GitRoot is { Length: > 0 } root
                    ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : Path.GetFileName(_lock.Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                Snapshot = new AgentSession(
                    ProviderId: ClaudeCodeProvider.ProviderId,
                    SessionId: _lock.SessionId,
                    ProcessId: _pid,
                    Cwd: _lock.Cwd,
                    GitRoot: _git?.GitRoot,
                    Branch: _git?.Branch,
                    Repository: string.IsNullOrEmpty(repository) ? null : repository,
                    Summary: _lock.Name,
                    Status: status,
                    LastUpdatedUtc: DateTime.UtcNow);
            }

            return changed;
        }

        private bool RefreshLock()
        {
            long lwt;
            try
            {
                if (!File.Exists(_lockPath))
                {
                    if (_lock is null) return false;
                    _lock = null;
                    return true;
                }
                lwt = File.GetLastWriteTimeUtc(_lockPath).Ticks;
            }
            catch
            {
                return false;
            }

            if (lwt == _lastLockLwt && _lock is not null)
                return false;

            var next = SessionLockReader.TryRead(_lockPath);
            _lastLockLwt = lwt;

            if (next is null && _lock is null) return false;
            if (next is not null && _lock is not null &&
                next.SessionId == _lock.SessionId &&
                string.Equals(next.Cwd, _lock.Cwd, StringComparison.OrdinalIgnoreCase) &&
                next.Name == _lock.Name &&
                string.Equals(next.Status, _lock.Status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _lock = next;
            return true;
        }
    }
}

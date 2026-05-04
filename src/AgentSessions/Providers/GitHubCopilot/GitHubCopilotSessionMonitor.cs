using System.Diagnostics;
using System.IO;
using AgentSessions.Providers.GitHubCopilot.Internal;

namespace AgentSessions.Providers.GitHubCopilot;

/// <summary>
/// Watches the Copilot CLI session-state directory and exposes a live snapshot
/// of running sessions. Hosts subscribe to <see cref="SnapshotChanged"/>.
/// </summary>
/// <remarks>
/// <para>
/// Detection: a running session is identified by an <c>inuse.&lt;PID&gt;.lock</c>
/// file inside its session-state folder. The PID is verified against the live
/// process list (and its start time is captured on first sighting) to skip
/// stale locks left by crashed processes and to defend against PID recycling.
/// </para>
/// <para>
/// State: <see cref="AgentSessionStatus.Processing"/> when at least one
/// in-flight tool, assistant message, or hook is observed in <c>events.jsonl</c>;
/// otherwise <see cref="AgentSessionStatus.Idle"/>.
/// </para>
/// </remarks>
public sealed class GitHubCopilotSessionMonitor : IAgentSessionStore
{
    private readonly string _sessionStateRoot;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<AgentSession> _snapshot = Array.Empty<AgentSession>();
    private int _started; // 0 = not started, 1 = started
    private Task? _loop;

    public event Action<IReadOnlyList<AgentSession>>? SnapshotChanged;

    public IReadOnlyList<AgentSession> CurrentSessions => _snapshot;

    public GitHubCopilotSessionMonitor()
        : this(CopilotPaths.SessionStateRoot, TimeSpan.FromMilliseconds(750)) { }

    public GitHubCopilotSessionMonitor(string sessionStateRoot, TimeSpan pollInterval)
    {
        _sessionStateRoot = sessionStateRoot;
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
        if (!Directory.Exists(_sessionStateRoot))
        {
            ReplaceSnapshotIfChanged(Array.Empty<AgentSession>());
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessions = new List<AgentSession>();
        bool changed = false;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(_sessionStateRoot);
        }
        catch
        {
            return;
        }

        foreach (var dir in dirs)
        {
            string sessionId = Path.GetFileName(dir);
            int? pid = TryReadInUsePid(dir);
            if (pid is null) continue;

            DateTime? startTimeUtc = TryGetProcessStartTimeUtc(pid.Value);
            if (startTimeUtc is null) continue; // process not alive or inaccessible

            seen.Add(sessionId);

            SessionTracker tracker;
            lock (_gate)
            {
                if (!_trackers.TryGetValue(sessionId, out tracker!))
                {
                    tracker = new SessionTracker(sessionId, dir, pid.Value, startTimeUtc.Value);
                    _trackers[sessionId] = tracker;
                    changed = true;
                }
                else if (tracker.ProcessId != pid.Value || tracker.ProcessStartTimeUtc != startTimeUtc.Value)
                {
                    // PID changed (re-launched into same session folder) or PID was recycled.
                    tracker.ResetForNewProcess(pid.Value, startTimeUtc.Value);
                    changed = true;
                }
            }

            if (tracker.Refresh())
                changed = true;

            if (tracker.Snapshot is { } s)
                sessions.Add(s);
        }

        // Remove trackers whose lock disappeared.
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

    private static int? TryReadInUsePid(string sessionDir)
    {
        string[] locks;
        try
        {
            locks = Directory.GetFiles(sessionDir, "inuse.*.lock");
        }
        catch
        {
            return null;
        }
        if (locks.Length == 0) return null;

        // Filename format: inuse.<PID>.lock
        foreach (var path in locks)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int dot = name.IndexOf('.');
            if (dot < 0) continue;
            if (int.TryParse(name[(dot + 1)..], out int pid))
                return pid;
        }
        return null;
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
    /// Per-session state held by the monitor. Knows how to tail its own
    /// events.jsonl and produce an immutable <see cref="AgentSession"/> snapshot.
    /// </summary>
    private sealed class SessionTracker
    {
        private readonly string _sessionId;
        private readonly string _sessionDir;
        private EventsJsonlTailer _tailer;

        private WorkspaceYamlReader.WorkspaceInfo? _workspace;
        private long _lastWorkspaceLwt = -1;
        private AgentSessionStatus? _lastStatus;

        public int ProcessId { get; private set; }
        public DateTime ProcessStartTimeUtc { get; private set; }
        public AgentSession? Snapshot { get; private set; }

        public SessionTracker(string sessionId, string sessionDir, int pid, DateTime startUtc)
        {
            _sessionId = sessionId;
            _sessionDir = sessionDir;
            ProcessId = pid;
            ProcessStartTimeUtc = startUtc;
            _tailer = new EventsJsonlTailer(Path.Combine(sessionDir, "events.jsonl"));
        }

        /// <summary>
        /// Resets all state (including the JSONL tailer) when the PID slot
        /// changes or a recycled PID is observed. The session folder may be
        /// re-bound to a fresh process instance.
        /// </summary>
        public void ResetForNewProcess(int pid, DateTime startUtc)
        {
            ProcessId = pid;
            ProcessStartTimeUtc = startUtc;
            _tailer = new EventsJsonlTailer(Path.Combine(_sessionDir, "events.jsonl"));
            _lastStatus = null;
            _workspace = null;
            _lastWorkspaceLwt = -1;
            Snapshot = null;
        }

        /// <summary>
        /// Re-reads workspace.yaml if it changed and polls events.jsonl.
        /// Returns true when the snapshot needs to be re-emitted.
        /// </summary>
        public bool Refresh()
        {
            bool changed = RefreshWorkspace();

            bool eventsRead = _tailer.Poll();
            var status = _tailer.IsProcessing
                ? AgentSessionStatus.Processing
                : AgentSessionStatus.Idle;

            if (status != _lastStatus)
            {
                _lastStatus = status;
                changed = true;
            }

            if (_workspace is null || string.IsNullOrEmpty(_workspace.Cwd))
            {
                Snapshot = null;
                return changed;
            }

            if (changed || eventsRead || Snapshot is null)
            {
                Snapshot = new AgentSession(
                    ProviderId: GitHubCopilotProvider.ProviderId,
                    SessionId: _sessionId,
                    ProcessId: ProcessId,
                    Cwd: _workspace.Cwd,
                    GitRoot: _workspace.GitRoot,
                    Branch: _workspace.Branch,
                    Repository: _workspace.Repository,
                    Summary: _workspace.Summary,
                    Status: status,
                    LastUpdatedUtc: DateTime.UtcNow);
            }

            return changed;
        }

        private bool RefreshWorkspace()
        {
            string path = Path.Combine(_sessionDir, "workspace.yaml");
            long lwt;
            try
            {
                if (!File.Exists(path))
                {
                    if (_workspace is null) return false;
                    _workspace = null;
                    return true;
                }
                lwt = File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                return false;
            }

            if (lwt == _lastWorkspaceLwt && _workspace is not null)
                return false;

            var next = WorkspaceYamlReader.TryRead(path);
            _lastWorkspaceLwt = lwt;
            if (next is null && _workspace is null) return false;
            if (next is not null && _workspace is not null &&
                string.Equals(next.Cwd, _workspace.Cwd, StringComparison.OrdinalIgnoreCase) &&
                next.Branch == _workspace.Branch &&
                next.GitRoot == _workspace.GitRoot &&
                next.Repository == _workspace.Repository &&
                next.Summary == _workspace.Summary)
            {
                return false;
            }

            _workspace = next;
            return true;
        }
    }
}


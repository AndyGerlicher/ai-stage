namespace AgentSessions;

/// <summary>
/// Combines several <see cref="IAgentSessionStore"/>s into a single store.
/// <see cref="CurrentSessions"/> is the concatenation of the inner stores'
/// snapshots, and <see cref="SnapshotChanged"/> fires whenever any inner
/// store changes. Disposing the aggregate disposes every inner store.
/// </summary>
public sealed class AggregateAgentSessionStore : IAgentSessionStore
{
    private readonly IAgentSessionStore[] _inner;
    private readonly Action<IReadOnlyList<AgentSession>>[] _innerHandlers;
    private readonly object _gate = new();
    private int _started;
    private bool _disposed;

    public event Action<IReadOnlyList<AgentSession>>? SnapshotChanged;

    public AggregateAgentSessionStore(IEnumerable<IAgentSessionStore> stores)
    {
        if (stores is null) throw new ArgumentNullException(nameof(stores));

        _inner = stores.ToArray();
        _innerHandlers = new Action<IReadOnlyList<AgentSession>>[_inner.Length];

        for (int i = 0; i < _inner.Length; i++)
        {
            // Capture nothing per-handler; we just need to re-emit the merged snapshot.
            _innerHandlers[i] = _ => RaiseSnapshot();
            _inner[i].SnapshotChanged += _innerHandlers[i];
        }
    }

    public IReadOnlyList<AgentSession> CurrentSessions
    {
        get
        {
            var merged = new List<AgentSession>();
            foreach (var s in _inner)
                merged.AddRange(s.CurrentSessions);
            return merged;
        }
    }

    public void Start()
    {
        if (System.Threading.Interlocked.Exchange(ref _started, 1) == 1)
            return;
        foreach (var s in _inner)
        {
            try { s.Start(); }
            catch { /* one bad inner store should not stop the others */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        for (int i = 0; i < _inner.Length; i++)
        {
            try { _inner[i].SnapshotChanged -= _innerHandlers[i]; } catch { }
            try { _inner[i].Dispose(); } catch { }
        }
    }

    private void RaiseSnapshot()
    {
        var merged = CurrentSessions;
        try { SnapshotChanged?.Invoke(merged); } catch { /* swallow handler exceptions */ }
    }
}

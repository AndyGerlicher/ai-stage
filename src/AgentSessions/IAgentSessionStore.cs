namespace AgentSessions;

/// <summary>
/// Read-only view of the currently running agent CLI sessions.
/// Hosts subscribe to <see cref="SnapshotChanged"/> to react to state changes.
/// </summary>
/// <remarks>
/// Implementations must treat snapshots as immutable: the list returned by
/// <see cref="CurrentSessions"/> and the list passed to
/// <see cref="SnapshotChanged"/> must not be mutated after the consumer can
/// observe them. Aggregators rely on this so they can read multiple inner
/// snapshots concurrently without locking.
/// </remarks>
public interface IAgentSessionStore : IDisposable
{
    /// <summary>The most recent snapshot of running sessions.</summary>
    IReadOnlyList<AgentSession> CurrentSessions { get; }

    /// <summary>
    /// Raised when the snapshot changes (sessions added/removed or status flipped).
    /// Handlers may be invoked on a background thread.
    /// </summary>
    event Action<IReadOnlyList<AgentSession>>? SnapshotChanged;

    /// <summary>Starts background tracking. Idempotent and thread-safe.</summary>
    void Start();
}

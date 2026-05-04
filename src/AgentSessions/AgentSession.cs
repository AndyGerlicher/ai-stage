namespace AgentSessions;

/// <summary>High-level state of an AI agent CLI session at a moment in time.</summary>
public enum AgentSessionStatus
{
    /// <summary>Session is open but has no in-flight tool/assistant operations.</summary>
    Idle,

    /// <summary>Session has at least one in-flight tool, assistant message, or hook.</summary>
    Processing,

    /// <summary>Session is blocked awaiting user input (e.g. permission prompt, AskUserQuestion).</summary>
    WaitingForInput,
}

/// <summary>
/// A snapshot of one running agent CLI session.
/// Immutable; a new instance is produced on every change.
/// </summary>
public sealed record AgentSession(
    string ProviderId,
    string SessionId,
    int ProcessId,
    string Cwd,
    string? GitRoot,
    string? Branch,
    string? Repository,
    string? Summary,
    AgentSessionStatus Status,
    DateTime LastUpdatedUtc);

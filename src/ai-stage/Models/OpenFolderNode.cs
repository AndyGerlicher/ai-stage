using AgentSessions;

namespace AiStage.Models;

/// <summary>
/// A folder with a live agent session whose path falls outside the scanned
/// repo/worktree list — i.e. a one-off opened via the "Open folder" toolbar
/// button. Surfaced in the "Open folders" section while a session is live and
/// removed once it closes.
/// </summary>
internal sealed class OpenFolderNode : ObservableNode
{
    private AgentSessionStatus? _agentState;
    private int _agentSessionCount;

    public required string Name { get; init; }
    public required string Path { get; init; }

    /// <summary>Aggregate Copilot session status for this row.</summary>
    public AgentSessionStatus? AgentState
    {
        get => _agentState;
        set => SetField(ref _agentState, value);
    }

    /// <summary>Number of Copilot sessions whose cwd matches this row's path.</summary>
    public int AgentSessionCount
    {
        get => _agentSessionCount;
        set => SetField(ref _agentSessionCount, value);
    }
}

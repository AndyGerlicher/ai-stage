using AgentSessions;

namespace Stage.Models;

/// <summary>
/// A git worktree linked to a parent repo. Tracked by branch name;
/// the folder name is an auto-numbered slot.
/// </summary>
internal sealed class WorktreeNode : ObservableNode
{
    private AgentSessionStatus? _copilotState;
    private int _copilotSessionCount;

    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Branch { get; init; }
    public DateTime? LastActivityUtc { get; init; }
    public required string ParentRepoPath { get; init; }

    /// <summary>Configured branch prefix (e.g. <c>"dev/angerlic/"</c>) used to strip the leading prefix from <see cref="DisplayName"/>.</summary>
    public required string BranchPrefix { get; init; }

    /// <summary>Short display name derived from the branch (strips the configured <see cref="BranchPrefix"/>).</summary>
    public string DisplayName => Branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
        ? Branch[BranchPrefix.Length..]
        : Branch;

    /// <summary>Short folder label like "wt\1" derived from the worktree path.</summary>
    public string FolderLabel
    {
        get
        {
            var leaf = System.IO.Path.GetFileName(Path);
            var parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path));
            if (parent is not null && parent.EndsWith(".wt", StringComparison.OrdinalIgnoreCase))
                return $"wt\\{leaf}";
            return leaf;
        }
    }

    /// <summary>Aggregate Copilot session status for this row, or <c>null</c> if no session is attached.</summary>
    public AgentSessionStatus? AgentState
    {
        get => _copilotState;
        set => SetField(ref _copilotState, value);
    }

    /// <summary>Number of Copilot sessions whose cwd matches this row's path.</summary>
    public int AgentSessionCount
    {
        get => _copilotSessionCount;
        set => SetField(ref _copilotSessionCount, value);
    }
}

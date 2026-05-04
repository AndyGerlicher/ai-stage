using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AgentSessions;

namespace Stage.Models;

/// <summary>
/// A repository discovered by the scanner. Worktrees are listed as children.
/// </summary>
internal sealed class RepoNode : ObservableNode
{
    private AgentSessionStatus? _copilotState;
    private int _copilotSessionCount;
    private RepoFilter? _filter;
    private ICollectionView? _worktreesView;

    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Branch { get; init; }
    public DateTime? LastActivityUtc { get; init; }
    public ObservableCollection<WorktreeNode> Worktrees { get; } = new();

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

    /// <summary>
    /// Shared filter applied to this node and its worktree view. Setting a new
    /// instance rewires change notifications and refreshes the worktree view.
    /// </summary>
    public RepoFilter? Filter
    {
        get => _filter;
        set
        {
            if (ReferenceEquals(_filter, value)) return;
            if (_filter is not null) _filter.Changed -= OnFilterChanged;
            _filter = value;
            if (_filter is not null) _filter.Changed += OnFilterChanged;
            _worktreesView?.Refresh();
        }
    }

    /// <summary>Filtered view over <see cref="Worktrees"/> bound by the tree template.</summary>
    public ICollectionView WorktreesView
    {
        get
        {
            if (_worktreesView is null)
            {
                _worktreesView = CollectionViewSource.GetDefaultView(Worktrees);
                _worktreesView.Filter = WorktreePassesFilter;
            }
            return _worktreesView;
        }
    }

    /// <summary>True when the repo itself or any worktree matches the current filter.</summary>
    public bool MatchesFilter()
    {
        if (_filter is null || _filter.IsEmpty) return true;
        if (_filter.Matches(Name)) return true;
        foreach (var w in Worktrees)
        {
            if (_filter.Matches(w.DisplayName)) return true;
        }
        return false;
    }

    private bool WorktreePassesFilter(object o)
    {
        if (_filter is null || _filter.IsEmpty) return true;
        // Repo name match → show all worktrees underneath.
        if (_filter.Matches(Name)) return true;
        return o is WorktreeNode w && _filter.Matches(w.DisplayName);
    }

    private void OnFilterChanged(object? sender, EventArgs e) => _worktreesView?.Refresh();
}



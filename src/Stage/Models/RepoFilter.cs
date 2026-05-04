using System.ComponentModel;

namespace Stage.Models;

/// <summary>
/// Shared filter state for the repo tree. A non-empty <see cref="Text"/>
/// matches case-insensitive substring against repo and worktree names.
/// </summary>
internal sealed class RepoFilter : INotifyPropertyChanged
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            var v = value ?? "";
            if (string.Equals(_text, v, StringComparison.Ordinal)) return;
            _text = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsEmpty => _text.Length == 0;

    public bool Matches(string? candidate)
    {
        if (IsEmpty) return true;
        return candidate is not null
            && candidate.Contains(_text, StringComparison.OrdinalIgnoreCase);
    }

    public event EventHandler? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
}

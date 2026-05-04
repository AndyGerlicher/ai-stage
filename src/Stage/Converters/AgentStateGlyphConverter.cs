using System.Globalization;
using System.Windows.Data;
using AgentSessions;

namespace Stage.Converters;

/// <summary>
/// Maps a <see cref="AgentSessionStatus"/> (or null) to a Segoe MDL2 glyph.
/// </summary>
internal sealed class AgentStateGlyphConverter : IValueConverter
{
    // E895 is "Stream" (busy/processing); F141 is "StatusCircleInner" (filled dot);
    // E7BA is "Warning" (triangle with !).
    private const string Processing = "\uE895";
    private const string Idle = "\uF141";
    private const string WaitingForInput = "\uE7BA";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            AgentSessionStatus.Processing => Processing,
            AgentSessionStatus.Idle => Idle,
            AgentSessionStatus.WaitingForInput => WaitingForInput,
            _ => string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

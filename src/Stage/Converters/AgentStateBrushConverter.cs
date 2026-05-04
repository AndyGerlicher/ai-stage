using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AgentSessions;

namespace Stage.Converters;

/// <summary>
/// Maps a <see cref="AgentSessionStatus"/> (or null) to a row brush.
/// Pass <c>"accent"</c> as ConverterParameter for the bright stripe/glyph
/// color, otherwise returns the dimmer row-fill tint.
/// </summary>
internal sealed class AgentStateBrushConverter : IValueConverter
{
    // Row fill (full-width background tint).
    private static readonly SolidColorBrush s_processingFill =
        Freeze(new SolidColorBrush(Color.FromRgb(0x5c, 0x3f, 0x12)));
    private static readonly SolidColorBrush s_idleFill =
        Freeze(new SolidColorBrush(Color.FromRgb(0x1d, 0x4a, 0x25)));
    private static readonly SolidColorBrush s_waitingFill =
        Freeze(new SolidColorBrush(Color.FromRgb(0x4a, 0x1d, 0x1d)));

    // Bright accent (left stripe + glyph foreground).
    private static readonly SolidColorBrush s_processingAccent =
        Freeze(new SolidColorBrush(Color.FromRgb(0xfb, 0xbf, 0x24)));
    private static readonly SolidColorBrush s_idleAccent =
        Freeze(new SolidColorBrush(Color.FromRgb(0x4a, 0xde, 0x80)));
    private static readonly SolidColorBrush s_waitingAccent =
        Freeze(new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71)));

    private static readonly SolidColorBrush s_none = Freeze(new SolidColorBrush(Colors.Transparent));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool accent = parameter is string s &&
            string.Equals(s, "accent", StringComparison.OrdinalIgnoreCase);

        return value switch
        {
            AgentSessionStatus.Processing => accent ? s_processingAccent : s_processingFill,
            AgentSessionStatus.Idle => accent ? s_idleAccent : s_idleFill,
            AgentSessionStatus.WaitingForInput => accent ? s_waitingAccent : s_waitingFill,
            _ => s_none,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}


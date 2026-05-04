using System.Globalization;
using System.Windows.Data;

namespace Stage.Converters;

/// <summary>
/// Formats a nullable DateTime (UTC) as a compact relative string:
/// "just now", "5m ago", "3h ago", "2d ago", or "yyyy-MM-dd" for anything older than 30 days.
/// </summary>
internal sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc)
            return string.Empty;

        DateTime now = DateTime.UtcNow;
        TimeSpan span = now - utc;

        if (span.TotalSeconds < 0) return "just now";
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

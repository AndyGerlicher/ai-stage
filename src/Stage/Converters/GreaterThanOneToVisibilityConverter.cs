using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Stage.Converters;

/// <summary>Visible when value is an <see cref="int"/> &gt; 1, else Collapsed.</summary>
internal sealed class GreaterThanOneToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 1 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

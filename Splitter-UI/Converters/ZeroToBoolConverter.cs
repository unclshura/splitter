using System.Globalization;
using Avalonia.Data.Converters;

namespace Splitter_UI.Converters;

public sealed class ZeroToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is int i && i == 0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

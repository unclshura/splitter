using System.Globalization;
using Avalonia.Data.Converters;

namespace Splitter_UI.Converters;

public sealed class ActionToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "crop"   => "\uf125", // FA7 crop
            "rotate" => "\uf2f1", // FA7 rotate
            _        => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

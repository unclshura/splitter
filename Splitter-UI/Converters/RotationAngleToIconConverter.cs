using System.Globalization;
using Avalonia.Data.Converters;

namespace Splitter_UI.Converters;

public sealed class RotationAngleToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            90  => "\uf2f9",  // FA7 (fa-rotate-left  / fa-arrow-rotate-left  / fa-undo)
            180 => "\uf2f1",  // FA7 (fa-sync-alt)
            270 => "\uf2ea",  // FA7 (fa-rotate-right / fa-arrow-rotate-right / fa-redo)
            _   => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

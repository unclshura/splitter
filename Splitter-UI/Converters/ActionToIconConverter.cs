using System.Globalization;
using Avalonia.Data.Converters;

namespace Splitter_UI.Converters;

public sealed class ActionToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return null;

        var p = System.Convert.ToInt32(value);
        
        return p == 0 
            ? "\uf125" // FA7 crop
            : "\uf2f1" // FA7 rotate
            ;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

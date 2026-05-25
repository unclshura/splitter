using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Splitter_UI.Converters;

public sealed class ConsoleColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConsoleColor c)
            return new SolidColorBrush(ToColor(c));

        return Brushes.White;
    }

    private static Color ToColor(ConsoleColor c) =>
        c switch
        {
            ConsoleColor.Black       => Colors.Black,
            ConsoleColor.DarkBlue    => Colors.DarkBlue,
            ConsoleColor.DarkGreen   => Colors.DarkGreen,
            ConsoleColor.DarkCyan    => Colors.DarkCyan,
            ConsoleColor.DarkRed     => Colors.DarkRed,
            ConsoleColor.DarkMagenta => Colors.DarkMagenta,
            ConsoleColor.DarkYellow  => Colors.Olive,
            ConsoleColor.Gray        => Colors.Gray,
            ConsoleColor.DarkGray    => Colors.DarkGray,
            ConsoleColor.Blue        => Colors.Blue,
            ConsoleColor.Green       => Colors.Green,
            ConsoleColor.Cyan        => Colors.Cyan,
            ConsoleColor.Red         => Colors.Red,
            ConsoleColor.Magenta     => Colors.Magenta,
            ConsoleColor.Yellow      => Colors.Yellow,
            ConsoleColor.White       => Colors.White,
            _                        => Colors.White
        };
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

using System.Globalization;

namespace splitter.probe;

public record VideoInfo(
    double Duration,
    int Width,
    int Height,
    double Fps,
    double Bitrate,
    string? SampleAspectRatio,
    string? DisplayAspectRatio,
    int Rotation = 0
)
{
    public Point2f Sar => ParseAspectRatio(SampleAspectRatio);
    public Point2f Dar => ParseAspectRatio(DisplayAspectRatio);

    private static Point2f ParseAspectRatio(string? sar)
    {
        if (string.IsNullOrWhiteSpace(sar))
            return new Point2f(1.0f, 1.0f);

        var parts = sar.Split(':');
        if (parts.Length != 2)
            return new(1.0f, 1.0f);

        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return new(num, den);
        }

        return new(1.0f, 1.0f);
    }
}

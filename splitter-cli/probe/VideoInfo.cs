namespace splitter.probe;

public record VideoInfo(
    double Duration,
    int Width,
    int Height,
    double Fps,
    double Bitrate,
    Point2f Sar,
    Point2f Dar,
    int Rotation = 0
);

using splitter;
using splitter.algo;
using splitter.probe;

public record SingleTask(
    SingleJob Job,
    VideoInfo Info,
    string    OutputFileName,
    int       SegmentIndex,
    int       TotalSegments,
    double    SegmentStart,
    double    SegmentLength,
    Func<int, ISegmentProcessor> ProcessorFactory
    );

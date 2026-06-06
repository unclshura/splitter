namespace splitter.algo;

public interface IVideoEnhancer : IAsyncDisposable
{
    int ResolutionMultiplier { get; }

    Task InitializeAsync(int width, int height, int window, CancellationToken token);

    // Returns true when an enhanced frame is ready
    bool TryProcessFrame(Mat input, out Mat output, CancellationToken token);

    // Flush remaining frames after input is finished
    int Flush(Span<Mat> outputFrames, CancellationToken token);
}

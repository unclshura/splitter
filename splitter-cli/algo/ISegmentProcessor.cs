namespace splitter.algo;

public interface IFrameProcessingState
{
}

public sealed record FrameProcessingResult(Mat? Image, List<DetectedPerson> Detected, DetectedPerson? Primary);

public interface ISegmentProcessor
{
    IFrameProcessingState InitSegment(SingleTask job, CancellationToken token);
    FrameProcessingResult GetNextProcessedFrame(IFrameProcessingState processorState, CancellationToken token);
    void FinishSegment(IFrameProcessingState processorState);

    Task ProcessSegment( SingleTask job, Action<FrameProcessingResult>? onFrameProcessed, CancellationToken token);
}

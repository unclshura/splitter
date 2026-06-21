namespace splitter.algo;

public interface IFrameProcessingState
{
}

public interface ISegmentProcessor
{
    IFrameProcessingState InitSegment(SingleTask job, CancellationToken token);
    Mat? GetNextProcessedFrame( IFrameProcessingState processorState, CancellationToken token);
    void FinishSegment(IFrameProcessingState processorState);

    Task ProcessSegment( SingleTask job, CancellationToken token);
}

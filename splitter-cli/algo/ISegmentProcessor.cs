namespace splitter.algo;

public interface ISegmentProcessor
{
    Task ProcessSegment( SingleTask job );
}

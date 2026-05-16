namespace splitter;

public interface ISegmentProcessor
{
    Task ProcessSegment( SingleTask job );
}

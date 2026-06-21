namespace splitter;

public interface IJobProcessor
{
    Task<List<SingleTask>> GenerateJobs(SingleJob job, bool estimateOnly, IReadOnlyCollection<Segment> predefinedSegments, CancellationToken token);
    Task<bool> ProcessJobs(List<SingleTask> tasks, bool singleThreaded, CancellationToken token);
}
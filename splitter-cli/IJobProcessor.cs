namespace splitter;

public interface IJobProcessor
{
    Task<List<SingleTask>> GenerateJobs(SingleJob job, bool estimateOnly, CancellationToken token);
    Task<bool> ProcessJobs(List<SingleTask> tasks, bool singleThreaded, CancellationToken token);
}
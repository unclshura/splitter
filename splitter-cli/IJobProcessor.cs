namespace splitter;

public interface IJobProcessor
{
    Task<List<SingleTask>> GenerateJobs(SingleJob job, bool estimateOnly);
    Task<bool> ProcessJobs(List<SingleTask> tasks, bool singleThreaded);
}
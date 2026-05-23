namespace Splitter_UI.Services;

public interface IProcessingService
{
    event Action<string, ProgressInfo>? ProgressChanged;

    Task ProcessAsync(IEnumerable<SingleJob> jobs, CancellationToken token);
}

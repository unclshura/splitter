namespace Splitter_UI.Services;

public sealed class ProcessingService : IProcessingService
{
    public event Action<string, ProgressInfo>? ProgressChanged;

    public async Task ProcessAsync(IEnumerable<SingleJob> jobs, CancellationToken token)
    {
        foreach (var job in jobs)
        {
            for (int i = 0; i <= 100; i += 20)
            {
                if (token.IsCancellationRequested)
                    return;

                var progress = new ProgressInfo { Percent = i };

                // Notify UI
                ProgressChanged?.Invoke(job.InputFile, progress);

                await Task.Delay(100, token);
            }
        }
    }
}

using splitter;

static partial class Program
{
    private static ILogger _logger = null!;

    static async Task<int> Main(string[] args)
    {
        Task? uiTask = null;

        var cmd = new CommandLine(args);
        if ( !cmd.IsValid)
            return -1;

        CancellationTokenSource? cts = null;

        if (cmd.Master.PlainText)
        {
            _logger = new TextLogger();
        }
        else
        {
            var logger = new SpectreConsoleLogger
            {
                Title = "Splitter",
            };
            _logger = logger;

            cts = new CancellationTokenSource();
            uiTask = logger.RunAsync(cts.Token);
        }

        var processor = new JobProcessor(_logger);

        if (cmd.Master.EstimateOnly)
            _logger.LogInfo("=== ESTIMATE MODE ===");

        var allJobs = new List<SingleTask>();
        foreach ( var job in cmd.Jobs )
        {
            var jobs = await processor.GenerateJobs(job, cmd.Master.EstimateOnly, [], CancellationToken.None);
            allJobs.AddRange(jobs);
        }

        if ( allJobs.Count == 0)
        {
            if ( !cmd.Master.EstimateOnly)
                _logger.LogWarn("No valid jobs to process.");
            return 0;
        }

        var success = await processor.ProcessJobs(allJobs, cmd.Master.SingleThreaded, CancellationToken.None);
        if (uiTask != null)
        {
            if ( cts != null )
                await cts.CancelAsync();
            await uiTask;
        }
        if (_logger is IDisposable disposable)
            disposable.Dispose();

        return success ? 1 : 0;
    }

}

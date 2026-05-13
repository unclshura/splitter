using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Spectre.Console;
using splitter;

static class Program
{
    private static ILogger _logger = null!;

    private record SingleTask(
        SingleJob Job,
        string OutputFileName,
        int SegmentIndex,
        int TotalSegments,
        double SegmentStart,
        double SegmentLength,
        Func<int, ISegmentProcessor> ProcessorFactory
        );

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
                NumberOfProcesses = cmd.Master.SingleThreaded ? 1 : Math.Max(1, Environment.ProcessorCount / 2) + 1
            };
            _logger = logger;

            cts = new CancellationTokenSource();
            uiTask = logger.RunAsync(cts.Token);
        }

        var allJobs = new List<SingleTask>();
        foreach ( var job in cmd.Jobs )
        {
            var jobs = await GenerateJobs(cmd, job);
            allJobs.AddRange(jobs);
        }

        if ( allJobs.Count == 0)
        {
            if ( !cmd.Master.EstimateOnly)
                LogWarn("No valid jobs to process.");
            return 0;
        }

        var success = await ProcessJobs(cmd, allJobs);
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

    private static async Task<List<SingleTask>> GenerateJobs(CommandLine cmd, SingleJob job)
    {
        var baseName = Path.GetFileNameWithoutExtension(job.InputFile);

        if (!File.Exists(job.InputFile))
        {
            LogError($"{baseName}: Input file not found.");
            return [];
        }

        if (!Directory.Exists(job.OutputFolder))
            Directory.CreateDirectory(job.OutputFolder);

        job.Mask ??= $"{baseName}_seg%03d.mp4";
        LogInfo($"{baseName}: Reading duration via ffprobe...");

        var duration = GetDuration(job.InputFile);
        if (duration <= 0)
        {
            LogError($"{baseName}: Could not read duration.");
            return [];
        }

        var target = job.OverrideTargetDuration ?? 58.0;

        int segments;
        double segmentLength;

        if (job.ForceFixed)
        {
            // Fixed chunk size, last one may be shorter
            segments = (int)Math.Ceiling(duration / target);
            segmentLength = target;
        }
        else
        {
            // Equalized segments
            segments = (int)Math.Ceiling(duration / target);
            segmentLength = duration / segments;
        }

        if (cmd.Master.EstimateOnly)
        {
            LogInfo("=== ESTIMATE MODE ===");
            LogInfo($"{baseName}: Total duration: {duration:F2}s");
            LogInfo($"{baseName}: Target duration: {target:F2}s");
            LogInfo($"{baseName}: Segments: {segments}");
            LogInfo(job.ForceFixed
                ? $"{baseName}: Fixed segment length: {segmentLength:F2}s (last may be shorter)"
                : $"{baseName}: Equalized segment length: {segmentLength:F2}s");
            return [];
        }

        LogInfo($"{baseName}: Duration: {duration:F2}s");
        LogInfo($"{baseName}: Segments: {segments}");
        LogInfo($"{baseName}: Equal segment length: {segmentLength:F3}s");

        Func<int, ISegmentProcessor> processorFactory;
        if (job.Crop != null)
        {
            processorFactory = i =>
            {
                IObjectDetector detector = job.Detect switch
                {
                    "face" => new UltraFaceDetector(_logger),
                    "body" => new YoloOnnxObjectDetector(_logger),
                    _      => throw new InvalidOperationException($"Unknown detector: {job.Detect}")
                };
                return new TrackingSplitter(i, job.Crop.Value.width, job.Crop.Value.height, cmd.Master.Debug, cmd.Master.PlainText, detector, job, _logger);
            };
        }
        else
        {
            processorFactory = i => new SimpleSplitter(i, _logger);
        }

        var jobs = Enumerable.Range(0, segments)
            .Select(i => new SingleTask
                (
                    Job              : job,
                    OutputFileName   : BuildOutputFileName(job.OutputFolder, job.Mask, i),
                    SegmentIndex     : i,
                    TotalSegments    : segments,
                    SegmentStart     : i * segmentLength,
                    SegmentLength    : (i == segments - 1)
                        ? Math.Max(0.1, duration - i * segmentLength)
                                     : segmentLength,
                    ProcessorFactory : processorFactory
                )
            )
            .ToList();

        return jobs;
    }

    private static async Task<bool> ProcessJobs(CommandLine cmd, List<SingleTask> tasks)
    { 

        if (cmd.Master.SingleThreaded)
        {
            LogInfo("Starting single-threaded splitting...");
            await RunSingleThreaded(tasks);
        }
        else
        {
            LogInfo("Starting multi-threaded splitting...");
            await RunMultiThreaded(tasks);
        }

        LogInfo("Done.");
        return true;
    }

    private static void LogInfo(string message)  => _logger.LogInfo(message);
    private static void LogWarn(string message)  => _logger.LogWarn(message);
    private static void LogError(string message) => _logger.LogError(message);
    private static void LogProgress(double progress, TimeSpan eta, double speed) => _logger.DrawProgress("Total", 0, progress, eta, speed);

    // -----------------------------
    // ffprobe
    // -----------------------------

    static double GetDuration(string inputFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "ffprobe",
            Arguments              = $"-v error -show_entries format=duration -of csv=p=0 \"{inputFile}\"",
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffprobe.");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (output != null &&
            double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
            return duration;

        return -1;
    }

    // -----------------------------
    // Multi-threaded splitting
    // -----------------------------

    static async Task RunMultiThreaded(List<SingleTask> jobs)
    {
        LogProgress(0.0, TimeSpan.Zero, 0.0);

        var maxDegree = Math.Max(1, Environment.ProcessorCount / 2);

        using var sem = new SemaphoreSlim(maxDegree);
        var tasks = new List<Task>();

        // Slot pool: 0..maxDegree-1
        var freeSlots = new ConcurrentQueue<int>(Enumerable.Range(0, maxDegree));

        var totalSegments     = jobs.Count;
        var processedSegments = 0;
        var totalDuration     = jobs.Sum(j => j.SegmentLength);
        var sw                = Stopwatch.StartNew();

        foreach (var job in jobs)
        {
            await sem.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                int slot = -1;

                try
                {
                    // Acquire a slot ID
                    while (!freeSlots.TryDequeue(out slot))
                        await Task.Yield();

                    await ProcessSegment(
                        job.ProcessorFactory,
                        job.Job.InputFile,
                        job.OutputFileName,
                        job.Job.Passthrough,
                        slot + 1,                     // <-- slot instead of SegmentIndex (+1 for totals)
                        job.SegmentStart,
                        job.SegmentLength
                    );

                    var processed = Interlocked.Increment(ref processedSegments);
                    var elapsed   = sw.Elapsed;
                    var eta       = TimeSpan.FromTicks(elapsed.Ticks * (totalSegments - processed) / processed);
                    var speed     = (processed * totalDuration) / elapsed.TotalSeconds;
                    LogProgress((double)processed / totalSegments, eta, speed);
                }
                finally
                {
                    // Return slot to pool
                    if (slot >= 0)
                        freeSlots.Enqueue(slot);

                    sem.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }


    // -----------------------------
    // Single-threaded splitting
    // -----------------------------

    static async Task RunSingleThreaded(List<SingleTask> jobs)
    {
        foreach (var job in jobs)
        {
            await ProcessSegment(
                job.ProcessorFactory,
                job.Job.InputFile,
                job.OutputFileName,
                job.Job.Passthrough,
                job.SegmentIndex,
                job.SegmentStart,
                job.SegmentLength
                );
        }

    }

    private static async Task ProcessSegment(Func<int, ISegmentProcessor> processorFactory, string inputFile, string outputFileName, string[] passthrough, int index, double start, double length)
    {
        var processor = processorFactory(index);
        try
        {
            await processor.ProcessSegment(inputFile, outputFileName, start, length, passthrough);
        }
        finally
        {
            if (processor is IDisposable disposable)
                disposable.Dispose();
        }
    }

    static string BuildOutputFileName(string folder, string mask, int index)
    {
        string fileName;

        if (mask.Contains("%03d"))
        {
            fileName = string.Format(mask.Replace("%03d", "{0:000}"), index);
        }
        else if (mask.Contains("%02d"))
        {
            fileName = string.Format(mask.Replace("%02d", "{0:00}"), index);
        }
        else if (mask.Contains("%d"))
        {
            fileName = string.Format(mask.Replace("%d", "{0}"), index);
        }
        else
        {
            // If no placeholder, append index
            var name = Path.GetFileNameWithoutExtension(mask);
            var ext = Path.GetExtension(mask);
            fileName = $"{name}_{index:000}{ext}";
        }

        return Path.Combine(folder, fileName);
    }

}

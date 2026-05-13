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
        int VideoWidth,
        int VideoHeight,
        double VideoFps,
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

        (double duration, int width, int height, double fps) = ProbeVideo(job.InputFile);
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
            LogInfo("=== ESTIMATE MODE ===");

        LogInfo($"{baseName}: Duration {duration:F2}s, {width}x{height} @ {fps:F3}fps");
        LogInfo($"{baseName}: Target duration: {target:F2}s");
        LogInfo($"{baseName}: Segments: {segments}");
        LogInfo(job.ForceFixed
            ? $"{baseName}: Fixed segment length: {segmentLength:F2}s (last may be shorter)"
            : $"{baseName}: Equalized segment length: {segmentLength:F2}s");

        if (cmd.Master.EstimateOnly)
            return [];

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
                return new TrackingSplitter(i, detector, job, _logger);
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
                    ProcessorFactory : processorFactory,
                    VideoWidth       : width,
                    VideoHeight      : height,
                    VideoFps         : fps
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

                    await ProcessSegment(job,slot + 1);

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
            await ProcessSegment(job, 0);
        }

    }

    private static async Task ProcessSegment(SingleTask t, int slot)
    {
        var processor = t.ProcessorFactory(slot);
        try
        {
            await processor.ProcessSegment(
                t.Job.InputFile,
                t.OutputFileName,
                t.SegmentStart,
                t.SegmentLength,
                t.VideoWidth,
                t.VideoHeight,
                t.VideoFps,
                t.Job.Passthrough);
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

    public static (double duration, int width, int height, double fps) ProbeVideo(string inputFile)
    {
        var args =
        "-v error " +
        "-select_streams v:0 " +
        "-show_entries format=duration " +
        "-show_entries stream=width,height,avg_frame_rate " +
        "-of default=noprint_wrappers=1:nokey=0 " +   // <-- IMPORTANT: include keys
        $"\"{inputFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = "ffprobe",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var p = new Process { StartInfo = psi };
        p.Start();

        var duration = -1.0;
        var width    = 0;
        var height   = 0;
        var fps      = 0.0;

        while (!p.StandardOutput.EndOfStream)
        {
            var line = p.StandardOutput.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("duration="))
            {
                var v = line.Substring("duration=".Length);
                double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out duration);
            }
            else if (line.StartsWith("width="))
            {
                var v = line.Substring("width=".Length);
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
            }
            else if (line.StartsWith("height="))
            {
                var v = line.Substring("height=".Length);
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
            }
            else if (line.StartsWith("avg_frame_rate="))
            {
                var v = line.Substring("avg_frame_rate=".Length);
                var parts = v.Split('/');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                    den != 0)
                {
                    fps = num / den;
                }
            }
        }

        p.WaitForExit();

        return (duration, width, height, fps);
    }


}

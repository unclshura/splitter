using System.Collections.Concurrent;
using System.Diagnostics;

namespace splitter;

public class JobProcessor(ILogger logger) : LoggingBase(logger, 0), IJobProcessor
{
    public async Task<List<SingleTask>> GenerateJobs(SingleJob job, bool estimateOnly)
    {
        var baseName = Path.GetFileNameWithoutExtension(job.InputFile);

        if (!File.Exists(job.InputFile))
        {
            LogError($"{baseName}: Input file not found.");
            return [];
        }

        if (!Directory.Exists(job.OutputFolder))
            Directory.CreateDirectory(job.OutputFolder);

        var info = await ProbeVideo.Probe(job.InputFile, job.RotateAuto);
        if (info.Duration <= 0)
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
            segments = (int)Math.Ceiling(info.Duration / target);
            segmentLength = target;
        }
        else
        {
            // Equalized segments
            segments = (int)Math.Ceiling(info.Duration / target);
            segmentLength = info.Duration / segments;
        }

        LogInfo($"{baseName}: Duration {info.Duration:F2}s, {info.Width}x{info.Height} @ {info.Fps:F3}fps {info.Bitrate / 1024:F0}kbps," +
            $" Target duration: {target:F2}s Segments: {segments} segment length: {segmentLength:F2}s {(job.ForceFixed ? " fixed" : "")}");

        if (estimateOnly)
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
                    Info: info,
                    OutputFileName   : BuildOutputFileName(job, i),
                    SegmentIndex     : i,
                    TotalSegments    : segments,
                    SegmentStart     : i * segmentLength,
                    SegmentLength    : (i == segments - 1)
                        ? Math.Max(0.1, info.Duration - i * segmentLength)
                                     : segmentLength,
                    ProcessorFactory : processorFactory
                )
            )
            .ToList();

        return jobs;
    }

    public async Task<bool> ProcessJobs(List<SingleTask> tasks, bool singleThreaded)
    {

        if (singleThreaded)
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

    private void LogProgress(double progress, TimeSpan eta, double speed) => _logger.DrawProgress("Total", 0, progress, eta, speed);

    // -----------------------------
    // ffprobe
    // -----------------------------

    // -----------------------------
    // Multi-threaded splitting
    // -----------------------------

    private async Task RunMultiThreaded(List<SingleTask> jobs)
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

                    await ProcessSegment(job, slot + 1);

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

    private async Task RunSingleThreaded(List<SingleTask> jobs)
    {
        foreach (var job in jobs)
        {
            await ProcessSegment(job, 0);
        }

    }

    private async Task ProcessSegment(SingleTask t, int slot)
    {
        var processor = t.ProcessorFactory(slot);
        try
        {
            await processor.ProcessSegment(t);
        }
        finally
        {
            if (processor is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static string BuildOutputFileName(SingleJob job, int index)
    {
        string fileName;

        fileName = Path.GetFileName(job.Mask ?? "[NAME]_seg[NN].[EXT]")
            .Replace("[NAME]", Path.GetFileNameWithoutExtension(job.InputFile))
            .Replace("[N]", index.ToString())
            .Replace("[NN]", index.ToString("00"))
            .Replace("[NNN]", index.ToString("000"))
            .Replace("[NNNN]", index.ToString("0000"))
            .Replace("[EXT]", Path.GetExtension(job.InputFile).TrimStart('.'))
            ;

        return Path.Combine(job.OutputFolder, fileName);
    }


}

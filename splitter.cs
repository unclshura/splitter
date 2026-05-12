using System.Diagnostics;
using System.Globalization;
using System.Text;
using splitter;

static class Program
{
    static async Task Main(string[] args)
    {
        var cmd = new CommandLine(args);

        var estimateOnly               = cmd.EstimateOnly;
        var forceFixed                 = cmd.ForceFixed;
        var passthrough                = cmd.Passthrough;
        var inputFile                  = cmd.InputFile;
        var outputFolder               = cmd.OutputFolder;
        (int width, int height)? crop  = cmd.Crop;
        string? mask                   = cmd.Mask;
        var debug                      = cmd.Debug;
        string? detect                 = cmd.Detect;
        double? overrideTargetDuration = cmd.OverrideTargetDuration;
        Logger.PlainText               = cmd.PlainText;

        if (!File.Exists(inputFile))
        {
            LogError("Input file not found.");
            return;
        }

        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        var baseName = Path.GetFileNameWithoutExtension(inputFile);
        var outputMask = mask ?? $"{baseName}_Seg%03d.mp4";

        LogInfo("Reading duration via ffprobe...");

        var duration = GetDuration(inputFile);
        if (duration <= 0)
        {
            LogError("Could not read duration.");
            return;
        }

        var target = overrideTargetDuration ?? 58.0;

        int segments;
        double segmentLength;

        if (forceFixed)
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

        if (estimateOnly)
        {
            LogInfo("=== ESTIMATE MODE ===");
            LogInfo($"Total duration: {duration:F2}s");
            LogInfo($"Target duration: {target:F2}s");
            LogInfo($"Segments: {segments}");
            LogInfo(forceFixed
                ? $"Fixed segment length: {segmentLength:F2}s (last may be shorter)"
                : $"Equalized segment length: {segmentLength:F2}s");
            return;
        }

        LogInfo($"Duration: {duration:F2}s");
        LogInfo($"Segments: {segments}");
        LogInfo($"Equal segment length: {segmentLength:F3}s");

        Func<int, ISegmentProcessor> processorFactory;
        if (crop != null)
        {
            processorFactory = i =>
            {
                IObjectDetector detector = detect switch
                {
                    "face" => new UltraFaceDetector(),
                    "body" => new YoloOnnxObjectDetector(),
                    _      => throw new InvalidOperationException($"Unknown detector: {detect}")
                };
                return new TrackingSplitter(i, crop.Value.width, crop.Value.height, debug, cmd.PlainText, detector, cmd);
            };
        }
        else
        {
            processorFactory = i => new SimpleSplitter(i);
        }
        if (cmd.SingleThreaded)
        {
            LogInfo("Starting single-threaded splitting...");
            await RunSingleThreaded(processorFactory, inputFile, outputFolder, outputMask, duration, segments, segmentLength, passthrough);
        }
        else
        {
            LogInfo("Starting multi-threaded splitting...");
            await RunMultiThreaded(processorFactory, inputFile, outputFolder, outputMask, duration, segments, segmentLength, passthrough);
        }

        LogInfo("Done.");
    }

    private static void LogInfo(string message)
        => Logger.LogInfo(message);

    private static void LogWarn(string message)
        => Logger.LogWarn(message);

    private static void LogError(string message)
        => Logger.LogError(message);

    // -----------------------------
    // ffprobe
    // -----------------------------

    static double GetDuration(string inputFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{inputFile}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
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

    static async Task RunMultiThreaded(
        Func<int, ISegmentProcessor> processorFactory,
        string inputFile,
        string outputFolder,
        string mask,
        double totalDuration,
        int segments,
        double segmentLength,
        string[] passthrough)
    {
        var jobs = Enumerable.Range(0, segments)
            .Select(i => new
            {
                Index = i,
                Start = i * segmentLength,
                Length = (i == segments - 1)
                    ? Math.Max(0.1, totalDuration - i * segmentLength)
                    : segmentLength
            })
            .ToList();

        var maxDegree = Math.Max(1, Environment.ProcessorCount / 2);
        using var sem = new SemaphoreSlim(maxDegree);
        var tasks = new List<Task>();

        foreach (var job in jobs)
        {
            await sem.WaitAsync();

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessSegment(processorFactory, inputFile, outputFolder, mask, passthrough, job.Index, job.Start, job.Length);
                }
                finally
                {
                    sem.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    static async Task RunSingleThreaded(
        Func<int, ISegmentProcessor> processorFactory,
        string inputFile,
        string outputFolder,
        string mask,
        double totalDuration,
        int segments,
        double segmentLength,
        string[] passthrough)
    {
        var jobs = Enumerable.Range(0, segments)
            .Select(i => new
            {
                Index = i,
                Start = i * segmentLength,
                Length = (i == segments - 1)
                    ? Math.Max(0.1, totalDuration - i * segmentLength)
                    : segmentLength
            })
            .ToList();

        foreach (var job in jobs)
        {
            await ProcessSegment(processorFactory, inputFile, outputFolder, mask, passthrough, job.Index, job.Start, job.Length);
        }

    }

    private static async Task ProcessSegment(Func<int, ISegmentProcessor> processorFactory, string inputFile, string outputFolder, string mask, string[] passthrough, int index, double start, double length)
    {
        var outputFile = BuildOutputFileName(outputFolder, mask, index);
        var processor = processorFactory(index);
        try
        {
            await processor.ProcessSegment(inputFile, outputFile, start, length, passthrough);
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

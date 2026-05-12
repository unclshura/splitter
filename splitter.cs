using System.Diagnostics;
using System.Globalization;
using System.Text;
using Spectre.Console;
using splitter;

static class Program
{
    private static ILogger _logger = null!;
    static async Task<int> Main(string[] args)
    {
        Task? uiTask = null;

        var cmd = new CommandLine(args);
        if ( !cmd.IsValid)
            return -1;

        if (cmd.PlainText)
        {
            _logger = new TextLogger();
        }
        else
        {
            Console.SetBufferSize(Console.WindowWidth, Console.BufferHeight);

            var logger = new SpectreConsoleLogger
            {
                Title = "Splitter",
                NumberOfProcesses = cmd.SingleThreaded ? 1 : Math.Max(1, Environment.ProcessorCount / 2)
            };
            _logger = logger;

            using var cts = new CancellationTokenSource();

            uiTask = logger.RunAsync(cts.Token);
        }

        var success = await ProcessAll(cmd);

        if (uiTask != null)
        {
            await uiTask;
        }
        if (_logger is IDisposable disposable)
            disposable.Dispose();

        return success ? 1 : 0;
    }

    private static async Task<bool> ProcessAll(CommandLine cmd)
    {
        if (!File.Exists(cmd.InputFile))
        {
            LogError("Input file not found.");
            return false;
        }

        if (!Directory.Exists(cmd.OutputFolder))
            Directory.CreateDirectory(cmd.OutputFolder);

        var baseName = Path.GetFileNameWithoutExtension(cmd.InputFile);
        var outputMask = cmd.Mask ?? $"{baseName}_Seg%03d.mp4";
        LogInfo("Reading duration via ffprobe...");

        var duration = GetDuration(cmd.InputFile);
        if (duration <= 0)
        {
            LogError("Could not read duration.");
            return false;
        }

        var target = cmd.OverrideTargetDuration ?? 58.0;

        int segments;
        double segmentLength;

        if (cmd.ForceFixed)
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

        if (cmd.EstimateOnly)
        {
            LogInfo("=== ESTIMATE MODE ===");
            LogInfo($"Total duration: {duration:F2}s");
            LogInfo($"Target duration: {target:F2}s");
            LogInfo($"Segments: {segments}");
            LogInfo(cmd.ForceFixed
                ? $"Fixed segment length: {segmentLength:F2}s (last may be shorter)"
                : $"Equalized segment length: {segmentLength:F2}s");
            return false;
        }

        LogInfo($"Duration: {duration:F2}s");
        LogInfo($"Segments: {segments}");
        LogInfo($"Equal segment length: {segmentLength:F3}s");

        Func<int, ISegmentProcessor> processorFactory;
        if (cmd.Crop != null)
        {
            processorFactory = i =>
            {
                IObjectDetector detector = cmd.Detect switch
                {
                    "face" => new UltraFaceDetector(_logger),
                    "body" => new YoloOnnxObjectDetector(_logger),
                    _      => throw new InvalidOperationException($"Unknown detector: {cmd.Detect}")
                };
                return new TrackingSplitter(i, cmd.Crop.Value.width, cmd.Crop.Value.height, cmd.Debug, cmd.PlainText, detector, cmd, _logger);
            };
        }
        else
        {
            processorFactory = i => new SimpleSplitter(i, _logger);
        }
        if (cmd.SingleThreaded)
        {
            LogInfo("Starting single-threaded splitting...");
            await RunSingleThreaded(processorFactory, cmd.InputFile, cmd.OutputFolder, outputMask, duration, segments, segmentLength, cmd.Passthrough);
        }
        else
        {
            LogInfo("Starting multi-threaded splitting...");
            await RunMultiThreaded(processorFactory, cmd.InputFile, cmd.OutputFolder, outputMask, duration, segments, segmentLength, cmd.Passthrough);
        }

        LogInfo("Done.");
        return true;
    }

    private static void LogInfo(string message)
        => _logger.LogInfo(message);

    private static void LogWarn(string message)
        => _logger.LogWarn(message);

    private static void LogError(string message)
        => _logger.LogError(message);

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

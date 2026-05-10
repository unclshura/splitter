using System.Diagnostics;
using System.Globalization;
using System.Text;
using splitter;

static class Program
{
    static int             _logLines        = 0;
    static bool            _plainText       = false;
    static readonly object _consoleLock     = new();
    static bool            _progressRunning = true;

    static void Main(string[] args)
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
        _plainText                     = cmd.PlainText;

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

        if (crop != null)
        {
            LogInfo("Starting multi-threaded face tracking crop and splitting...");
            RunMultiThreadedCrop(inputFile, outputFolder, outputMask, duration, segments, segmentLength, passthrough, crop.Value.width, crop.Value.height, debug, detect);
        }
        else
        {
            LogInfo("Starting multi-threaded ffmpeg splitting...");
            RunMultiThreadedSplit(inputFile, outputFolder, outputMask, duration, segments, segmentLength, passthrough);
        }

        LogSuccess("Done.");
        _progressRunning = false;
        // Move cursor below progress area
        lock (_consoleLock)
        {
            Console.SetCursorPosition(0, _logLines + 4);
            Console.WriteLine();
        }
    }


    // -----------------------------
    // Logging + Progress UI
    // -----------------------------

    static void Log(string prefix, ConsoleColor color, string msg)
    {
        lock (_consoleLock)
        {
            if (_plainText)
            {
                Console.WriteLine($"{prefix} {msg}");
            }
            else
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"{prefix} {msg}");
                Console.ResetColor();
                _logLines++;
            }
        }
    }

    static void LogInfo(string msg)    => Log("[INFO]", ConsoleColor.Cyan, msg);
    static void LogSuccess(string msg) => Log("[ OK ]", ConsoleColor.Green, msg);
    static void LogWarn(string msg)    => Log("[WARN]", ConsoleColor.Yellow, msg);
    static void LogError(string msg)   => Log("[ERR ]", ConsoleColor.Red, msg);

    static void DrawProgress(double progress, TimeSpan eta, double speed)
    {
        if ( _plainText )
            return;

        lock (_consoleLock)
        {
            var width = Math.Max(20, Console.WindowWidth - 20);
            var filled = (int)(progress * width);
            if (filled < 0) filled = 0;
            if (filled > width) filled = width;

            var barLine = _logLines + 1;
            var infoLine = _logLines + 2;

            // Progress bar with 24-bit color (green)
            Console.SetCursorPosition(0, barLine);
            Console.Write("\u001b[38;2;0;255;0m[");
            Console.Write(new string('#', filled));
            Console.Write(new string('-', width - filled));
            Console.Write("]\u001b[0m");

            // Info line: percentage, ETA, speed
            Console.SetCursorPosition(0, infoLine);
            var etaStr = eta.TotalSeconds < 0 || double.IsInfinity(eta.TotalSeconds)
                ? "ETA: --:--"
                : $"ETA: {eta:mm\\:ss}";
            var speedStr = double.IsNaN(speed) || double.IsInfinity(speed)
                ? "Speed: -.-x"
                : $"Speed: {speed:F2}x";

            var info = $"{progress * 100:0.0}%  {etaStr}  {speedStr}   ";
            Console.Write("\u001b[38;2;180;180;180m" + info.PadRight(Console.WindowWidth - 1) + "\u001b[0m");
        }
    }

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

    static void RunMultiThreadedSplit(
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

        var completed = 0;
        var sw = Stopwatch.StartNew();

        // Progress thread
        var progressThread = new Thread(() =>
        {
            while (_progressRunning)
            {
                var progress = segments == 0 ? 0 : (double)completed / segments;
                var processedSeconds = completed * segmentLength;
                var speed = sw.Elapsed.TotalSeconds > 0
                    ? processedSeconds / sw.Elapsed.TotalSeconds
                    : 0;

                var remainingSeconds = (totalDuration - processedSeconds) / Math.Max(speed, 0.0001);
                if (remainingSeconds < 0) remainingSeconds = 0;
                var eta = TimeSpan.FromSeconds(remainingSeconds);

                DrawProgress(progress, eta, speed);
                Thread.Sleep(200);
            }
        })
        {
            IsBackground = true
        };
        progressThread.Start();

        var maxDegree = Math.Max(1, Environment.ProcessorCount / 2);

        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
            job =>
            {
                var outputFile = BuildOutputFileName(outputFolder, mask, job.Index);
                RunFFmpegSegment(inputFile, outputFile, job.Start, job.Length, passthrough);
                Interlocked.Increment(ref completed);
            });

        sw.Stop();
        _progressRunning = false;
        progressThread.Join();
        DrawProgress(1.0, TimeSpan.Zero, totalDuration / Math.Max(sw.Elapsed.TotalSeconds, 0.0001));
    }

    static void RunSingleThreadedSplit(
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

        var completed = 0;
        var sw = Stopwatch.StartNew();

        // Progress thread
        var progressThread = new Thread(() =>
        {
            while (_progressRunning)
            {
                var progress = segments == 0 ? 0 : (double)completed / segments;
                var processedSeconds = completed * segmentLength;
                var speed = sw.Elapsed.TotalSeconds > 0
                ? processedSeconds / sw.Elapsed.TotalSeconds
                : 0;

                var remainingSeconds = (totalDuration - processedSeconds) / Math.Max(speed, 0.0001);
                if (remainingSeconds < 0) remainingSeconds = 0;
                var eta = TimeSpan.FromSeconds(remainingSeconds);

                DrawProgress(progress, eta, speed);
                Thread.Sleep(200);
            }
        })
        {
            IsBackground = true
        };
        progressThread.Start();

        // --- SINGLE THREADED LOOP ---
        foreach (var job in jobs)
        {
            var outputFile = BuildOutputFileName(outputFolder, mask, job.Index);
            RunFFmpegSegment(inputFile, outputFile, job.Start, job.Length, passthrough);
            completed++;
        }

        sw.Stop();
        _progressRunning = false;
        progressThread.Join();
        DrawProgress(1.0, TimeSpan.Zero, totalDuration / Math.Max(sw.Elapsed.TotalSeconds, 0.0001));
    }

    // -----------------------------
    // Multi-threaded cropping
    // -----------------------------
    private static void RunMultiThreadedCrop(
        string inputFile,
        string outputFolder,
        string outputMask,
        double duration,
        int segments,
        double segmentLength,
        string[] passthrough,
        int width,
        int height,
        bool showDebugOverlay,
        string? detect)
    {
        var tracker = new TrackingSplitter(Log, DrawProgress);

        var jobs = Enumerable.Range(0, segments)
        .Select(i => new
        {
            Index = i,
            Start = i * segmentLength,
            Length = (i == segments - 1)
                ? Math.Max(0.1, duration - i * segmentLength)
                : segmentLength
        })
        .ToList();

        var completed = 0;
        var sw = Stopwatch.StartNew();
        _progressRunning = true;

        // --- PROGRESS THREAD ---
        var progressThread = new Thread(() =>
        {
            while (_progressRunning)
            {
                var progress = segments == 0 ? 0 : (double)completed / segments;
                var processedSeconds = completed * segmentLength;

                var speed = sw.Elapsed.TotalSeconds > 0
                ? processedSeconds / sw.Elapsed.TotalSeconds
                : 0;

                var remainingSeconds = (duration - processedSeconds) / Math.Max(speed, 0.0001);
                if (remainingSeconds < 0) remainingSeconds = 0;

                var eta = TimeSpan.FromSeconds(remainingSeconds);

                DrawProgress(progress, eta, speed);
                Thread.Sleep(200);
            }
        })
        {
            IsBackground = true
        };
        progressThread.Start();

        // --- PARALLEL EXECUTION ---
        var maxDegree = Math.Max(1, Environment.ProcessorCount / 2);

        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
            async job =>
            {
                var outputFile = BuildOutputFileName(outputFolder, outputMask, job.Index);
                using IDisposable detector = detect switch
                {
                    "face" => new UltraFaceDetector(Log, DrawProgress),
                    "body" => new YoloOnnxObjectDetector(Log, DrawProgress),
                    _      => throw new InvalidOperationException($"Unknown detector: {detect}")
                };

                // Run the face-tracking cropper
                await tracker.TrackAndExtract(
                    inputFile,
                    outputFile,
                    (IObjectDetector)detector,
                    TimeSpan.FromSeconds(job.Start),
                    TimeSpan.FromSeconds(job.Length),
                    width,
                    height,
                    passthrough,
                    showDebugOverlay);

                Interlocked.Increment(ref completed);
            });

        // --- CLEANUP ---
        sw.Stop();
        _progressRunning = false;
        progressThread.Join();

        var finalSpeed = duration / Math.Max(sw.Elapsed.TotalSeconds, 0.0001);
        DrawProgress(1.0, TimeSpan.Zero, finalSpeed);
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

    static void RunFFmpegSegment(string inputFile, string outputFile, double start, double length, string[] passthrough)
    {
        var pass = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";

        var args =
            $"-ss {start.ToString(CultureInfo.InvariantCulture)} -i \"{inputFile}\" -t {length.ToString(CultureInfo.InvariantCulture)} -c copy {pass} \"{outputFile}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg.");
        proc.StandardError.ReadToEnd(); // swallow output
        proc.WaitForExit();
    }
}

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static int logLines = 0;
    static readonly object consoleLock = new();
    static bool progressRunning = true;

    static void Main(string[] args)
    {
        double? overrideTargetDuration = null;
        bool estimateOnly = false;
        bool forceFixed = false;


        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help"))
        {
            PrintHelp();
            return;
        }

        // Extract passthrough parameters after "--"
        string[] passthrough = Array.Empty<string>();
        int passthroughIndex = Array.IndexOf(args, "--");

        if (passthroughIndex >= 0)
        {
            if (passthroughIndex < args.Length - 1)
                passthrough = args.Skip(passthroughIndex + 1).ToArray();

            args = args.Take(passthroughIndex).ToArray();
        }

        if (args.Length < 2)
        {
            LogError("Missing required parameters.");
            PrintHelp();
            return;
        }

        string inputFile = args[0];
        string outputFolder = args[1];
        string? mask = null;

        foreach (var arg in args.Skip(2))
        {
            if (arg.StartsWith("--mask="))
            {
                mask = arg.Substring("--mask=".Length);
            }
            else if (arg.StartsWith("--duration="))
            {
                string dur = arg.Substring("--duration=".Length);
                overrideTargetDuration = ParseDuration(dur);
                if (overrideTargetDuration <= 0)
                {
                    LogError($"Invalid --duration value: {dur}");
                    return;
                }
            }
            else if (arg == "--estimate")
            {
                estimateOnly = true;
            }
            else if (arg == "--force")
            {
                forceFixed = true;
            }
        }

        if (!File.Exists(inputFile))
        {
            LogError("Input file not found.");
            return;
        }

        if (!Directory.Exists(outputFolder))
            Directory.CreateDirectory(outputFolder);

        string baseName = Path.GetFileNameWithoutExtension(inputFile);
        string outputMask = mask ?? $"{baseName}_Seg%03d.mp4";

        LogInfo("Reading duration via ffprobe...");

        double duration = GetDuration(inputFile);
        if (duration <= 0)
        {
            LogError("Could not read duration.");
            return;
        }

        double target = overrideTargetDuration ?? 58.0;

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

        LogInfo("Starting multi-threaded ffmpeg splitting...");

        RunMultiThreadedSplit(inputFile, outputFolder, outputMask, duration, segments, segmentLength, passthrough);

        LogSuccess("Done.");
        progressRunning = false;
        // Move cursor below progress area
        lock (consoleLock)
        {
            Console.SetCursorPosition(0, logLines + 4);
            Console.WriteLine();
        }
    }

    // -----------------------------
    // Logging + Progress UI
    // -----------------------------

    static void Log(string prefix, ConsoleColor color, string msg)
    {
        lock (consoleLock)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{prefix} {msg}");
            Console.ResetColor();
            logLines++;
        }
    }

    static void LogInfo(string msg) => Log("[INFO]", ConsoleColor.Cyan, msg);
    static void LogSuccess(string msg) => Log("[ OK ]", ConsoleColor.Green, msg);
    static void LogWarn(string msg) => Log("[WARN]", ConsoleColor.Yellow, msg);
    static void LogError(string msg) => Log("[ERR ]", ConsoleColor.Red, msg);

    static void DrawProgress(double progress, TimeSpan eta, double speed)
    {
        lock (consoleLock)
        {
            int width = Math.Max(20, Console.WindowWidth - 20);
            int filled = (int)(progress * width);
            if (filled < 0) filled = 0;
            if (filled > width) filled = width;

            int barLine = logLines + 1;
            int infoLine = logLines + 2;

            // Progress bar with 24-bit color (green)
            Console.SetCursorPosition(0, barLine);
            Console.Write("\u001b[38;2;0;255;0m[");
            Console.Write(new string('#', filled));
            Console.Write(new string('-', width - filled));
            Console.Write("]\u001b[0m");

            // Info line: percentage, ETA, speed
            Console.SetCursorPosition(0, infoLine);
            string etaStr = eta.TotalSeconds < 0 || double.IsInfinity(eta.TotalSeconds)
                ? "ETA: --:--"
                : $"ETA: {eta:mm\\:ss}";
            string speedStr = double.IsNaN(speed) || double.IsInfinity(speed)
                ? "Speed: -.-x"
                : $"Speed: {speed:F2}x";

            string info = $"{progress * 100:0.0}%  {etaStr}  {speedStr}   ";
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
        string? output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (output != null &&
            double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
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

        int completed = 0;
        var sw = Stopwatch.StartNew();

        // Progress thread
        var progressThread = new Thread(() =>
        {
            while (progressRunning)
            {
                double progress = segments == 0 ? 0 : (double)completed / segments;
                double processedSeconds = completed * segmentLength;
                double speed = sw.Elapsed.TotalSeconds > 0
                    ? processedSeconds / sw.Elapsed.TotalSeconds
                    : 0;

                double remainingSeconds = (totalDuration - processedSeconds) / Math.Max(speed, 0.0001);
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

        int maxDegree = Math.Max(1, Environment.ProcessorCount / 2);

        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegree },
            job =>
            {
                string outputFile = BuildOutputFileName(outputFolder, mask, job.Index);
                RunFFmpegSegment(inputFile, outputFile, job.Start, job.Length, passthrough);
                Interlocked.Increment(ref completed);
            });

        sw.Stop();
        progressRunning = false;
        progressThread.Join();
        DrawProgress(1.0, TimeSpan.Zero, totalDuration / Math.Max(sw.Elapsed.TotalSeconds, 0.0001));
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
            string name = Path.GetFileNameWithoutExtension(mask);
            string ext = Path.GetExtension(mask);
            fileName = $"{name}_{index:000}{ext}";
        }

        return Path.Combine(folder, fileName);
    }

    static void RunFFmpegSegment(string inputFile, string outputFile, double start, double length, string[] passthrough)
    {
        string pass = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";

        string args =
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

    static double ParseDuration(string text)
    {
        text = text.Trim().ToLowerInvariant();

        // Case 1: pure number to seconds
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double sec))
            return sec;

        // Case 2: Ns (seconds)
        if (text.EndsWith("s") && double.TryParse(text[..^1], out sec))
            return sec;

        // Case 3: NmMs (minutes + seconds)
        // Examples: 2m30s, 1m5s, 10m0s
        int mIndex = text.IndexOf('m');
        int sIndex = text.IndexOf('s');

        if (mIndex > 0 && sIndex > mIndex)
        {
            string mPart = text[..mIndex];
            string sPart = text[(mIndex + 1)..sIndex];

            if (double.TryParse(mPart, out double minutes) &&
                double.TryParse(sPart, out double seconds))
            {
                return minutes * 60 + seconds;
            }
        }

        throw new FormatException($"Invalid duration format: {text}");
    }

    // -----------------------------
    // Help
    // -----------------------------

    static void PrintHelp()
    {
        Console.WriteLine(@"
Usage:
  splitter <input.mp4> <output_folder> [options] [--] <ffmpeg passthrough>

Options:
  --mask=<pattern>       Output filename pattern.
                         Default: <OriginalName>_Seg%03d.mp4
                         Supports %03d or %d for segment index.

  --duration=<value>     Override target segment duration.
                         Accepted formats:
                           Ns      - N seconds
                           NmMs    - N minutes M seconds
                           N       - N seconds (plain number)

                         Examples:
                           --duration=90s
                           --duration=2m30s
                           --duration=45

                         Without --force:
                           Segments are equalized so all have same length.

  --force                Use fixed segment duration exactly as given.
                         Last segment may be shorter.
                         Default: OFF

  --estimate             Print calculated segment information and exit.
                         No splitting is performed.

Passthrough:
  Anything after -- is passed directly to ffmpeg.

Examples:
  splitter video.mp4 out/
  splitter video.mp4 out/ --duration=90s
  splitter video.mp4 out/ --duration=2m30s --mask=""Part%03d.mp4""
  splitter video.mp4 out/ --estimate
  splitter video.mp4 out/ --force --duration=45 -- -an -sn

Description:
  Splits a video into equal or fixed-length segments using multi-threaded
  ffmpeg execution. Supports ETA, speed, and rich progress display.
");
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenCvSharp;

namespace splitter;

public class SingleJob
{
    public string                     InputFile              { get; set; } = null!;
    public string                     OutputFolder           { get; set; } = null!;
    public (int width, int height)?   Crop                   { get; set; }
    public Point2f?                   GravitateTo            { get; set; }
    public string?                    Mask                   { get; set; }
    public bool                       Debug                  { get; set; }
    public string?                    Detect                 { get; set; }
    public double?                    OverrideTargetDuration { get; set; }
    public string[]                   Passthrough            { get; set; } = [];
    public bool                       PlainText              { get; set; }
    public bool                       EstimateOnly           { get; set; }
    public bool                       ForceFixed             { get; set; }
    public bool                       SingleThreaded         { get; set; }
    public int?                       Rotate                 { get; set; }
    public Dictionary<string, string> Parameters             { get; set; } = [];

    public void Override<T>(ref T member, string name)
    {
        if (!Parameters.TryGetValue(name, out var raw))
            return;

        try
        {
            // Convert.ChangeType handles int, float, double, etc.
            var converted = (T)Convert.ChangeType(
            raw,
            typeof(T),
            CultureInfo.InvariantCulture
        );

            member = converted;
        }
        catch
        {
            Console.WriteLine($"Invalid value for parameter '{name}': {raw}");
        }
    }

}

public sealed class CommandLine
{
    public SingleJob Master { get; } = new SingleJob();
    public SingleJob[] Jobs { get; }

    public bool IsValid => !string.IsNullOrEmpty(Master.OutputFolder) && Jobs.Length > 0;

    public CommandLine(string[] args)
    {
        Master.InputFile = "";
        Master.OutputFolder = "";
        Jobs = [];

        if (args.Length == 0 || args.Contains("--help"))
        {
            PrintHelp();
            return;
        }

        // Extract passthrough parameters after "--"
        var passthroughIndex = Array.IndexOf(args, "--");

        if (passthroughIndex >= 0)
        {
            if (passthroughIndex < args.Length - 1)
                Master.Passthrough = args.Skip(passthroughIndex + 1).ToArray();

            args = args.Take(passthroughIndex).ToArray();
        }

        if (args.Length < 1)
        {
            PrintHelp();
            Console.WriteLine("[ERROR]: Missing required parameters.");
            return;
        }

        var inputFiles = new List<string>();

        foreach (var arg in args)
        {
            if (!arg.StartsWith("-"))
            {
                inputFiles.Add(arg);
            }
            else if ( arg.StartsWith("--file="))
            {
                var fileName = arg.Substring("--file=".Length);
                if (File.Exists(arg))
                    LoadFile(fileName, inputFiles);
            }
            else if (arg.StartsWith("--mask="))
            {
                Master.Mask = arg.Substring("--mask=".Length);
            }
            else if (arg.StartsWith("--out="))
            {
                Master.OutputFolder = arg.Substring("--out=".Length);
            }
            else if (arg.StartsWith("--detect="))
            {
                Master.Detect = arg.Substring("--detect=".Length).ToLowerInvariant();
            }
            else if (arg =="--rotate")
            {
                Master.Rotate = 90;
            }
            else if (arg.StartsWith("--rotate="))
            {
                var val = arg.Substring("--rotate=".Length);
                if (int.TryParse(val, out var degrees) && (degrees == 90 || degrees == 180 || degrees == 270))
                    Master.Rotate = degrees;
                else
                    throw new FormatException($"Invalid --rotate value: {val}");
            }
            else if (arg.StartsWith("--crop="))
            {
                Master.Crop = ParseCrop(arg.Substring("--crop=".Length));
            }
            else if (arg == "--crop")
            {
                Master.Crop = ParseCrop("");
            }
            else if (arg == "--text")
            {
                Master.PlainText = true;
            }
            else if (arg == "--debug")
            {
                Master.Debug = true;
            }
            else if (arg == "--single-thread")
            {
                Master.SingleThreaded = true;
            }
            else if (arg.StartsWith("--gravitate="))
            {
                var val = arg.Substring("--gravitate=".Length);
                Master.GravitateTo = ParseGravitate(val);
            }
            else if (arg.StartsWith("--duration="))
            {
                var dur = arg.Substring("--duration=".Length);
                Master.OverrideTargetDuration = ParseDuration(dur);
                if (Master.OverrideTargetDuration <= 0)
                {
                    Console.WriteLine($"Invalid --duration value: {dur}");
                    return;
                }
            }
            else if (arg.StartsWith("-p:", StringComparison.Ordinal))
            {
                var spec = arg.Substring("-p:".Length);
                if (!TryParseParameter(spec, out var key, out var value))
                {
                    Console.WriteLine($"Invalid -p parameter: {spec}");
                    return;
                }

                Master.Parameters[key] = value;
            }
            else if (arg == "--estimate")
            {
                Master.EstimateOnly = true;
            }
            else if (arg == "--force")
            {
                Master.ForceFixed = true;
            }
        }

        var files = inputFiles.SelectMany(x => FileMaskExpander.Expand(x));

        Jobs = files.Select(x => new SingleJob
        {
            InputFile              = x,
            OutputFolder           = Master.OutputFolder,
            Crop                   = Master.Crop,
            GravitateTo            = Master.GravitateTo,
            Mask                   = Master.Mask,
            Debug                  = Master.Debug,
            Detect                 = Master.Detect,
            OverrideTargetDuration = Master.OverrideTargetDuration,
            Passthrough            = Master.Passthrough,
            PlainText              = Master.PlainText,
            EstimateOnly           = Master.EstimateOnly,
            ForceFixed             = Master.ForceFixed,
            SingleThreaded         = Master.SingleThreaded,
            Rotate                 = Master.Rotate,
            Parameters             = new Dictionary<string, string>(Master.Parameters)
        }).ToArray();

        if ( Jobs.Length == 0)
        {
            PrintHelp();
            Console.WriteLine("[ERROR]:No valid input files found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Master.OutputFolder))
        {
            var firstInput = Jobs[0].InputFile;
            Master.OutputFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(firstInput))!, "Splitter");
            Console.WriteLine($"Using default output folder: {Master.OutputFolder}");
        }
    }

    private void LoadFile(string fileName, List<string> inputFiles)
    {
        if (!File.Exists(fileName))
        {
            Console.WriteLine($"File not found: {fileName}");
            return;
        }
        var lines = File.ReadAllLines(fileName);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                inputFiles.AddRange(FileMaskExpander.Expand(trimmed));
        }
    }

    private static bool TryParseParameter(string spec, out string key, out string value)
    {
        key = "";
        value = "";

        var idx = spec.IndexOf('=');
        if (idx <= 0 || idx == spec.Length - 1)
            return false;

        key = spec.Substring(0, idx).Trim();
        value = spec.Substring(idx + 1).Trim();

        return key.Length > 0;
    }

    private static Point2f? ParseGravitate(string value)
    {
        // Expected format: "<x>:<y>"
        var parts = value.Split(':');
        if (parts.Length != 2)
            return null;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return null;

        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return null;

        // Normalized range check (0.0–1.0)
        if (x < 0f || x > 1f || y < 0f || y > 1f)
            return null;

        return new Point2f(x, y);
    }

    private static (int width, int height)? ParseCrop(string v)
    {
        // Default vertical Full HD for YouTube Shorts
        const int defaultW = 607;
        const int defaultH = 1080;

        // Empty or whitespace → default crop
        if (string.IsNullOrWhiteSpace(v))
            return (defaultW, defaultH);

        var s = v.Trim().ToLowerInvariant();

        // Expected format: "WWWxHHH"
        var parts = s.Split('x');
        if (parts.Length != 2)
            return null;

        var okW = int.TryParse(parts[0], out var w);
        var okH = int.TryParse(parts[1], out var h);

        if (!okW || !okH || w <= 0 || h <= 0)
            return null;

        return (w, h);
    }

    static double ParseDuration(string text)
    {
        text = text.Trim().ToLowerInvariant();

        // Case 1: pure number to seconds
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var sec))
            return sec;

        // Case 2: Ns (seconds)
        if (text.EndsWith("s") && double.TryParse(text[..^1], out sec))
            return sec;

        // Case 3: NmMs (minutes + seconds)
        // Examples: 2m30s, 1m5s, 10m0s
        var mIndex = text.IndexOf('m');
        var sIndex = text.IndexOf('s');

        if (mIndex > 0 && sIndex > mIndex)
        {
            var mPart = text[..mIndex];
            var sPart = text[(mIndex + 1)..sIndex];

            if (double.TryParse(mPart, out var minutes) &&
                double.TryParse(sPart, out var seconds))
            {
                return minutes * 60 + seconds;
            }
        }

        throw new FormatException($"Invalid duration format: {text}");
    }
    public static void PrintHelp()
    {
        Console.WriteLine(@"
Usage:
  splitter [<input.mp4> ...] [options] [--] <ffmpeg passthrough>

Options:
  --out=<folder>         Output folder for segments.
                         Default: same folder as input video + ""Splitter"".

  --file=<path>          Input names or file masks (e.g. ""videos/*.mp4"").
                         If not specified, the first non-option argument is used as input.

  --mask=<pattern>       Output filename pattern.
                         Default: [NAME]_seg[NN].[EXT]
                         Supports [NAME], [N], [NN], [NNN], [NNNN], [EXT] placeholders.

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
                           Default: maximum of 58 seconds, but segments are equalized so all have same length.

  --force                Use fixed segment duration exactly as given.
                         Last segment may be shorter.
                         Default: OFF

  --rotate=<degrees>     Rotate video by specified degrees (90, 180, 270).
                         Useful for videos with incorrect orientation metadata.

  --estimate             Print calculated segment information and exit.
                         No splitting is performed.

  --crop[=<w:h>]         Crop video to width w and height h, with face tracking.
                         Useful to making YouTube Shorts or TikToks from horizontal video.
                         Default: 607x1080 (vertical video cropped from Full HD original)

  --detect=<name>        Object detector to use for tracking.
                         Values: face (UltraFace), body (YoloOnnx, default), none (no tracking, just a center)

  --gravitate=<x:y>      Gravitate towards a specific point (x, y) in the video frame.
                         Coordinates are normalized (0.0 to 1.0).
                         Example: --gravitate=0.2:0.5 (gravitate towards left-center)

  --text                 Display log in plain text.

  --single-thread        Run in single-threaded mode (no parallel ffmpeg processes).
                         Useful for debugging or if system is resource-constrained.

  --debug                Show debug overlay during face tracking.

  -p:<name>=<value>      Set a custom parameter for the object detector.
                         Example: -p:confidence=0.5

                         Tracking splitter defaults:
                            DropoutToleranceFrames = 20;
                            EmaFactor              = 0.65;
                            CameraEasing           = 0.03;
                            LostFreezeFrames       = 60;   

Passthrough:
  Anything after -- is passed directly to ffmpeg.

input.mp4 can be a file mask, e.g. ""videos/*.mp4"". Output files will be named based on the input filename and the --mask pattern if provided.

Examples:
  splitter vertical-video.mp4
  splitter vertical-video.mp4 --duration=90s
  splitter vertical-video.mp4 --duration=2m30s --mask=""[NAME]_[NNNN].mp4""
  splitter vertical-video.mp4 --estimate
  splitter vertical-video.mp4 --force --duration=45 -- -an -sn
  splitter horizontal-video.mp4 --out=Cropped/ --crop
  splitter --file=file_names.txt --out=Cropped/ --crop --detect=body

Description:
  Splits a video into equal or fixed-length segments using multi-threaded
  ffmpeg execution. Supports ETA, speed, and rich progress display.
");
    }
}

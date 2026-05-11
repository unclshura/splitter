using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace splitter;

public sealed class CommandLine
{
    public string InputFile               { get; private init; }
    public string OutputFolder            { get; private init; }
    public (int width, int height)? Crop  { get; private init; }
    public string? Mask                   { get; private init; }
    public bool Debug                     { get; private init; }
    public string? Detect                 { get; private init; }
    public double? OverrideTargetDuration { get; private init; }
    public string[] Passthrough           { get; private init; } = Array.Empty<string>();
    public bool PlainText                 { get; private init; }
    public bool EstimateOnly              { get; private init; }
    public bool ForceFixed                { get; private init; }
    public bool SingleThreaded            { get; private init; }

    public bool IsValid => !string.IsNullOrEmpty(InputFile) && !string.IsNullOrEmpty(OutputFolder);

    public CommandLine(string[] args)
    {
        InputFile = "";
        OutputFolder = "";

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
                Passthrough = args.Skip(passthroughIndex + 1).ToArray();

            args = args.Take(passthroughIndex).ToArray();
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Missing required parameters.");
            PrintHelp();
            return;
        }

        InputFile = args[0];
        OutputFolder = args[1];

        foreach (var arg in args.Skip(2))
        {
            if (arg.StartsWith("--mask="))
            {
                Mask = arg.Substring("--mask=".Length);
            }
            else if (arg.StartsWith("--detect="))
            {
                Detect = arg.Substring("--detect=".Length).ToLowerInvariant();
            }
            else if (arg.StartsWith("--crop="))
            {
                Crop = ParseCrop(arg.Substring("--crop=".Length));
            }
            else if (arg == "--crop")
            {
                Crop = ParseCrop("");
            }
            else if (arg == "--text")
            {
                PlainText = true;
            }
            else if (arg == "--debug")
            {
                Debug = true;
            }
            else if (arg == "--single-thread")
            {
                SingleThreaded = true;
            }
            else if (arg.StartsWith("--duration="))
            {
                var dur = arg.Substring("--duration=".Length);
                OverrideTargetDuration = ParseDuration(dur);
                if (OverrideTargetDuration <= 0)
                {
                    Console.WriteLine($"Invalid --duration value: {dur}");
                    return;
                }
            }
            else if (arg == "--estimate")
            {
                EstimateOnly = true;
            }
            else if (arg == "--force")
            {
                ForceFixed = true;
            }
        }
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

  --crop[=<w:h>]         Crop video to width w and height h, with face tracking.
                         Useful to making YouTube Shorts or TikToks from horizontal video.
                         Default: 607x1080 (vertical video cropped from Full HD original)

  --detect=<name>        Object detector to use for tracking.
                         Values: face (UltraFace), body (YoloOnnx, default), none (no tracking, just a center)

  --text                 Display log in plain text.

  --single-thread        Run in single-threaded mode (no parallel ffmpeg processes).
                         Useful for debugging or if system is resource-constrained.

  --debug                Show debug overlay during face tracking.

Passthrough:
  Anything after -- is passed directly to ffmpeg.

Examples:
  splitter vertical-video.mp4 out/
  splitter vertical-video.mp4 out/ --duration=90s
  splitter vertical-video.mp4 out/ --duration=2m30s --mask=""Part%03d.mp4""
  splitter vertical-video.mp4 out/ --estimate
  splitter vertical-video.mp4 out/ --force --duration=45 -- -an -sn
  splitter horizontal-video.mp4 out/ --crop

Description:
  Splits a video into equal or fixed-length segments using multi-threaded
  ffmpeg execution. Supports ETA, speed, and rich progress display.
");
    }
}

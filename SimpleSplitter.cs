using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using FFmpeg.AutoGen;

namespace splitter;

public class SimpleSplitter(int segmentNo, ILogger logger) : LoggingBase(logger, segmentNo), ISegmentProcessor
{
    public async Task ProcessSegment(SingleTask job)
    {
        string inputFile                     = job.Job.InputFile;
        string outputFile                    = job.OutputFileName;
        double start                         = job.SegmentStart;
        double length                        = job.SegmentLength;
        int videoWidth                       = job.Info.Width;
        int videoHeight                      = job.Info.Height;
        double fps                           = job.Info.Fps;
        string[] ffmpegPassthroughParameters = job.Job.Passthrough;
        
        var pass = ffmpegPassthroughParameters.Length > 0 ? string.Join(" ", ffmpegPassthroughParameters) : "";

        string args;
        var rotation = GetRotationFilter(job.Job.Rotate);
        if (rotation == null)
        {
            args =
                $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFile}\" " +
                $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
                $"-c copy {pass} \"{outputFile}\" -y";
        }
        else
        {
            // Rotation → must re-encode
            args =
                $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFile}\" " +
                $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
                $"-vf \"{rotation}\" " +
                "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
                "-c:a copy " +
                $"{pass} \"{outputFile}\" -y";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg.");

        var name = Path.GetFileNameWithoutExtension(outputFile);
        ShowFFMpegProgress(length, proc, name);

        proc.WaitForExit();
        
        ClearProgress();

        if (proc.ExitCode != 0)
            LogError($"Segment {name} FFmpeg encoding failed");
        else
            LogInfo($"Segment {name} processing completed");
    }

    string? GetRotationFilter(int? degrees) =>
        degrees switch
        {
            90 => "transpose=1",
            180 => "rotate=PI",
            270 => "transpose=2",
            _ => null
        };


    private void ShowFFMpegProgress(double length, Process proc, string name)
    {
        var sw = Stopwatch.StartNew();

        string? line;
        while ((line = proc.StandardError.ReadLine()) != null)
        {
            // Look for "time=00:00:03.52"
            var idx = line.IndexOf("time=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;

            var timeStr = ExtractTimestamp(line, idx + 5);
            if (timeStr == null)
                continue;

            if (!TryParseFfmpegTime(timeStr, out var current))
                continue;

            var progress = current.TotalSeconds / length;
            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;

            var elapsed = sw.Elapsed;
            var speed = current.TotalSeconds > 0
            ? current.TotalSeconds / elapsed.TotalSeconds
            : 0;

            var remaining = length - current.TotalSeconds;
            var etaSeconds = speed > 0 ? remaining / speed : remaining;
            var eta = TimeSpan.FromSeconds(etaSeconds);

            DrawProgress(name, progress, eta, speed);
        }
    }

    private static string? ExtractTimestamp(string line, int startIndex)
    {
        // FFmpeg formats: HH:MM:SS.xx
        // We read until whitespace
        int end = startIndex;
        while (end < line.Length && !char.IsWhiteSpace(line[end]))
            end++;

        if (end <= startIndex)
            return null;

        return line[startIndex..end];
    }

    private static bool TryParseFfmpegTime(string s, out TimeSpan ts)
    {
        // FFmpeg uses "00:00:03.52"
        return TimeSpan.TryParseExact(
            s,
            @"hh\:mm\:ss\.ff",
            CultureInfo.InvariantCulture,
            out ts);
    }

}

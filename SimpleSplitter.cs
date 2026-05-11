using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace splitter;

public class SimpleSplitter(int segmentNo) : LoggingBase(segmentNo), ISegmentProcessor
{
    public async Task ProcessSegment(string inputFile, string outputFile, double start, double length, string[] passthrough)
    {
        RunFFmpegSegment(inputFile, outputFile, start, length, passthrough);
    }

    private void RunFFmpegSegment(
        string inputFile,
        string outputFile,
        double start,
        double length,
        string[] passthrough)
    {
        var pass = passthrough.Length > 0 ? string.Join(" ", passthrough) : "";

        var args =
        $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
        $"-i \"{inputFile}\" " +
        $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
        $"-c copy {pass} \"{outputFile}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg.");

        ShowFFMpegProgress(length, proc);

        proc.WaitForExit();
    }

    private void ShowFFMpegProgress(double length, Process proc)
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

            DrawProgress(progress, eta, speed);
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

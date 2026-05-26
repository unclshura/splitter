using System.Diagnostics;
using System.Globalization;

namespace splitter;

public class SimpleSplitter(int segmentNo, ILogger logger) : LoggingBase(logger, segmentNo), ISegmentProcessor
{
    public async Task ProcessSegment(SingleTask job, CancellationToken token)
    {
        string inputFile  = job.Job.InputFile;
        string outputFile = job.OutputFileName;
        double start      = job.SegmentStart;
        double length     = job.SegmentLength;

        var rotation = GetRotationFilter(job.Job.Rotate);

        string args;

        if (rotation == null)
        {
            // Copy path: keep original SAR/DAR exactly as in source
            args =
                $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFile}\" " +
                $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
                $"-c copy {string.Join(" ", job.Job.Passthrough)} " +
                $"\"{outputFile}\" -y";
        }
        else
        {
            var sarArg = "";
            var darArg = "";

            var sar = job.Info.SampleAspectRatio; // e.g. "4:3"
            if (sar != null)
            {
                // Rotation path: must re-encode and recompute DAR

                long sarNum = Convert.ToInt64(job.Info.Sar.X);
                long sarDen = Convert.ToInt64(job.Info.Sar.Y);

                // After rotation, width/height swap
                int w = job.Info.Width;
                int h = job.Info.Height;

                if (job.Job.Rotate == 90 || job.Job.Rotate == 270)
                {
                    (w, h) = (h, w);
                }

                // Compute DAR = (w * sarNum) : (h * sarDen)
                var darNum = w * sarNum;
                var darDen = h * sarDen;

                // Reduce fraction
                long Gcd(long a, long b)
                {
                    while (b != 0) (a, b) = (b, a % b);
                    return a;
                }
                var g = Gcd(darNum, darDen);
                darNum /= g;
                darDen /= g;

                sarArg = $"-vf \"{rotation},setsar={sarNum}:{sarDen}\" ";
                darArg = $"-aspect {darNum}:{darDen} ";
            }
            else
                sarArg = $"-vf \"{rotation}\" ";

            args =
                $"-ss {start.ToString(CultureInfo.InvariantCulture)} " +
                $"-i \"{inputFile}\" " +
                $"-t {length.ToString(CultureInfo.InvariantCulture)} " +
                sarArg + darArg +
                "-c:v h264_nvenc -preset p4 -b:v 8M -pix_fmt yuv420p " +
                "-c:a copy " +
                $"{string.Join(" ", job.Job.Passthrough)} " +
                $"\"{outputFile}\" -y";
        }
        var psi = new ProcessStartInfo
        {
            FileName              = "ffmpeg",
            Arguments             = args,
            RedirectStandardError = true,
            UseShellExecute       = false,
            CreateNoWindow        = true
        };

        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start ffmpeg.");

        var name = Path.GetFileNameWithoutExtension(outputFile);
        await ShowFFMpegProgress(length, proc, name, token);

        await proc.WaitForExitAsync(token);

        ClearProgress(name);

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

    private static long Gcd(long a, long b)
    {
        a = Math.Abs(a);
        b = Math.Abs(b);

        while (b != 0)
        {
            long t = b;
            b = a % b;
            a = t;
        }

        return a;
    }

    private async Task ShowFFMpegProgress(double length, Process proc, string name, CancellationToken token)
    {
        var sw = Stopwatch.StartNew();

        string? line;
        while ((line = await proc.StandardError.ReadLineAsync(token)) != null)
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

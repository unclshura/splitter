using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace splitter;

public record VideoInfo(
    double Duration,
    int Width,
    int Height,
    double Fps,
    double Bitrate
);

public static class ProbeVideo
{
    public static VideoInfo Probe(string inputFile)
    {
        var args =
        "-v error " +
        "-select_streams v:0 " +
        "-show_entries format=duration " +
        "-show_entries stream=width,height,avg_frame_rate,bit_rate " +
        "-of default=noprint_wrappers=1:nokey=0 " +   // <-- IMPORTANT: include keys
        $"\"{inputFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = "ffprobe",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var p = new Process { StartInfo = psi };
        p.Start();

        var duration = -1.0;
        var width    = 0;
        var height   = 0;
        var fps      = 0.0;
        var bitrate  = 0.0;

        while (!p.StandardOutput.EndOfStream)
        {
            var line = p.StandardOutput.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("duration="))
            {
                var v = line.Substring("duration=".Length);
                double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out duration);
            }
            else if (line.StartsWith("width="))
            {
                var v = line.Substring("width=".Length);
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
            }
            else if (line.StartsWith("bit_rate="))
            {
                var v = line.Substring("bit_rate=".Length);
                double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out bitrate);
            }
            else if (line.StartsWith("height="))
            {
                var v = line.Substring("height=".Length);
                int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
            }
            else if (line.StartsWith("avg_frame_rate="))
            {
                var v = line.Substring("avg_frame_rate=".Length);
                var parts = v.Split('/');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                    den != 0)
                {
                    fps = num / den;
                }
            }
        }

        p.WaitForExit();

        return new(duration, width, height, fps, bitrate);
    }
}

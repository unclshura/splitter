using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace splitter.probe;

public static class ProbeVideo
{
    static ProbeVideo()
    {
        _ffprobeJsonOptions.Converters.Add(new FlexibleDoubleConverter());
        _ffprobeJsonOptions.Converters.Add(new FlexibleIntConverter());
        _ffprobeJsonOptions.Converters.Add(new FlexibleLongConverter());
    }

    public static async Task<VideoInfo> Probe(string inputFile, bool detectRotation, CancellationToken token)
    {
        var info = await ProbeSize(inputFile, token);
        if (detectRotation)
        {
            var rotation = await ProbeRotation(inputFile, info.Duration, token);
            info = info with { Rotation = rotation };
        }

        return info;
    }

    private static async Task<int> ProbeRotation(string inputFile, double duration, CancellationToken token)
        => await new VideoRotationSampler(null).DetectRotationAsync(inputFile, duration, token);

    private static readonly JsonSerializerOptions _ffprobeJsonOptions =
        new ()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyProperties    = false,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            UnknownTypeHandling         = JsonUnknownTypeHandling.JsonElement
        };

    private static async Task<VideoInfo> ProbeSize(string inputFile, CancellationToken token)
    {
        var args =
            "-v error " +
            "-show_streams " +
            "-show_format " +
            "-of json " +
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

        var json = await p.StandardOutput.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);

        var result = JsonSerializer.Deserialize<FfprobeResult>(json, _ffprobeJsonOptions);
        var stream = result?.Streams?.FirstOrDefault();
        var format = result?.Format;

        var duration = format?.Duration ?? 0.0;
        var width    = stream?.Width ?? 0;
        var height   = stream?.Height ?? 0;

        var fps = 0.0;
        if (!string.IsNullOrWhiteSpace(stream?.Avg_frame_rate))
        {
            var parts = stream.Avg_frame_rate.Split('/');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                den != 0)
            {
                fps = num / den;
            }
        }

        var bitrate = stream?.Bit_rate ?? 0.0;

        return new VideoInfo(duration, width, height, fps, bitrate, stream?.Sample_aspect_ratio, stream?.Display_aspect_ratio);
    }

}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace splitter;

public record VideoInfo(
    double Duration,
    int Width,
    int Height,
    double Fps,
    double Bitrate,
    double SarX,
    double SarY,
    int Rotation = 0
);

public static class ProbeVideo
{
    static ProbeVideo()
    {
        _ffprobeJsonOptions.Converters.Add(new FlexibleDoubleConverter());
        _ffprobeJsonOptions.Converters.Add(new FlexibleIntConverter());
        _ffprobeJsonOptions.Converters.Add(new FlexibleLongConverter());
    }

    public static async Task<VideoInfo> Probe(SingleJob job)
    {
        var info = ProbeSize(job.InputFile);
        if ( job.RotateAuto)
        {
            var rotation = await ProbeRotation(job, info.Duration);
            info = info with { Rotation = rotation };
        }

        return info;
    }

    private static async Task<int> ProbeRotation(SingleJob job, double duration)
    {
        var rotation = await new VideoRotationSampler(job).DetectRotationAsync(job.InputFile, duration);
        return rotation;
    }

    public sealed class FfprobeResult
    {
        public List<FfprobeStream>? Streams { get; set; }
        public FfprobeFormat? Format { get; set; }
    }

    public sealed class FfprobeFormat
    {
        public string? Filename { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Nb_streams { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Nb_programs { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Nb_stream_groups { get; set; }

        public string? Format_name { get; set; }
        public string? Format_long_name { get; set; }

        [JsonConverter(typeof(FlexibleDoubleConverter))]
        public double? Start_time { get; set; }

        [JsonConverter(typeof(FlexibleDoubleConverter))]
        public double? Duration { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Size { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Bit_rate { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Probe_score { get; set; }

        public Dictionary<string, string>? Tags { get; set; }
    }


    public sealed class FfprobeStream
    {
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Index { get; set; }

        public string? Codec_name { get; set; }
        public string? Codec_long_name { get; set; }
        public string? Profile { get; set; }
        public string? Codec_type { get; set; }
        public string? Codec_tag_string { get; set; }
        public string? Codec_tag { get; set; }
        public string? Mime_codec_string { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Width { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Height { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Coded_width { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Coded_height { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Closed_captions { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Film_grain { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Has_b_frames { get; set; }

        public string? Sample_aspect_ratio { get; set; }
        public string? Display_aspect_ratio { get; set; }

        public string? Pix_fmt { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Level { get; set; }

        public string? Color_range { get; set; }
        public string? Color_space { get; set; }
        public string? Color_transfer { get; set; }
        public string? Color_primaries { get; set; }
        public string? Chroma_location { get; set; }
        public string? Field_order { get; set; }

        public string? Is_avc { get; set; }
        public string? Nal_length_size { get; set; }

        public string? Id { get; set; }
        public string? R_frame_rate { get; set; }
        public string? Avg_frame_rate { get; set; }
        public string? Time_base { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Start_pts { get; set; }

        [JsonConverter(typeof(FlexibleDoubleConverter))]
        public double? Start_time { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Duration_ts { get; set; }

        [JsonConverter(typeof(FlexibleDoubleConverter))]
        public double? Duration { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Bit_rate { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Max_bit_rate { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Bits_per_raw_sample { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Bits_per_sample { get; set; }

        [JsonConverter(typeof(FlexibleLongConverter))]
        public long? Nb_frames { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Extradata_size { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Channels { get; set; }

        public string? Channel_layout { get; set; }
        public string? Sample_fmt { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Sample_rate { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Initial_padding { get; set; }

        public string? Disposition_raw { get; set; }
        public Dictionary<string, int>? Disposition { get; set; }

        public Dictionary<string, string>? Tags { get; set; }

        public string? Language { get; set; }
        public string? Title { get; set; }

        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Bits_per_coded_sample { get; set; }

        public string? Codec_time_base { get; set; }

        [JsonConverter(typeof(FlexibleDoubleConverter))]
        public double? Start_pts_time { get; set; }

        [JsonConverter(typeof(FlexibleDoubleConverter))]
        public double? Duration_time { get; set; }

        public string? Extradata { get; set; }
        public string? Default { get; set; }
        public string? Forced { get; set; }
    }

    private static readonly JsonSerializerOptions _ffprobeJsonOptions =
        new ()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyProperties    = false,
            AllowTrailingCommas         = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            UnknownTypeHandling         = JsonUnknownTypeHandling.JsonElement
        };

    private static VideoInfo ProbeSize(string inputFile)
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

        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        var result = JsonSerializer.Deserialize<FfprobeResult>(json, _ffprobeJsonOptions);
        var stream = result?.Streams?.FirstOrDefault();
        var format = result?.Format;

        var duration = format?.Duration ?? 0.0;
        var width    = stream?.Width ?? 0;
        var height   = stream?.Height ?? 0;

        double fps = 0.0;
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

        var (sarX, sarY) = ParseSar(stream?.Sample_aspect_ratio);

        return new VideoInfo(duration, width, height, fps, bitrate, sarX, sarY);
    }

    static double ParseDouble(string? s)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;

        return 0.0;
    }

    private static (double x, double y) ParseSar(string? sar)
    {
        if (string.IsNullOrWhiteSpace(sar))
            return (1.0, 1.0);

        var parts = sar.Split(':');
        if (parts.Length != 2)
            return (1.0, 1.0);

        if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return (num, den);
        }

        return (1.0, 1.0);
    }

}

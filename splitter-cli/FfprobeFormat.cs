using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace splitter;

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

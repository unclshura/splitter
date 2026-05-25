using System.Text.Json.Serialization;

namespace splitter.probe;

public sealed class FfprobeStream
{
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Index                           { get; set; }

    public string? Codec_name                   { get; set; }
    public string? Codec_long_name              { get; set; }
    public string? Profile                      { get; set; }
    public string? Codec_type                   { get; set; }
    public string? Codec_tag_string             { get; set; }
    public string? Codec_tag                    { get; set; }
    public string? Mime_codec_string            { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Width                           { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Height                          { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Coded_width                     { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Coded_height                    { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Closed_captions                 { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Film_grain                      { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Has_b_frames                    { get; set; }

    public string? Sample_aspect_ratio          { get; set; }
    public string? Display_aspect_ratio         { get; set; }

    public string? Pix_fmt                      { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Level                           { get; set; }

    public string? Color_range                  { get; set; }
    public string? Color_space                  { get; set; }
    public string? Color_transfer               { get; set; }
    public string? Color_primaries              { get; set; }
    public string? Chroma_location              { get; set; }
    public string? Field_order                  { get; set; }

    public string? Is_avc                       { get; set; }
    public string? Nal_length_size              { get; set; }

    public string? Id                           { get; set; }
    public string? R_frame_rate                 { get; set; }
    public string? Avg_frame_rate               { get; set; }
    public string? Time_base                    { get; set; }

    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? Start_pts                      { get; set; }

    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Start_time                   { get; set; }

    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? Duration_ts                    { get; set; }

    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Duration                     { get; set; }

    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? Bit_rate                       { get; set; }

    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? Max_bit_rate                   { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Bits_per_raw_sample             { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Bits_per_sample                 { get; set; }

    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? Nb_frames                      { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Extradata_size                  { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Channels                        { get; set; }

    public string? Channel_layout               { get; set; }
    public string? Sample_fmt                   { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Sample_rate                     { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Initial_padding                 { get; set; }

    public string? Disposition_raw              { get; set; }
    public Dictionary<string, int>? Disposition { get; set; }

    public Dictionary<string, string>? Tags     { get; set; }

    public string? Language                     { get; set; }
    public string? Title                        { get; set; }

    [JsonConverter(typeof(FlexibleIntConverter))]
    public int? Bits_per_coded_sample           { get; set; }

    public string? Codec_time_base              { get; set; }

    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Start_pts_time               { get; set; }

    [JsonConverter(typeof(FlexibleDoubleConverter))]
    public double? Duration_time                { get; set; }

    public string? Extradata                    { get; set; }
    public string? Default                      { get; set; }
    public string? Forced                       { get; set; }
}

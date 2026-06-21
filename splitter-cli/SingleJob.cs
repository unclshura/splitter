using System.Globalization;

namespace splitter;

public record Segment(double Start, double End);

public class SingleJob
{
    /// <summary>
    /// File path of the input video. This is required for each job and should be
    /// set to a valid video file path. The splitter will read this file, analyze it, 
    /// and split it into segments based on the specified parameters. 
    /// The output segments will be saved in the OutputFolder with names 
    /// derived from this input file and the Mask pattern if provided.
    /// </summary>
    public string                     InputFile              { get; set; } = null!;
    /// <summary>
    /// Output folder where the split segments will be saved. This should be set 
    /// to a valid directory path.
    /// </summary>
    public string                     OutputFolder           { get; set; } = null!;
    /// <summary>
    /// Crop parameters. Width and height for cropping the video. If set, the 
    /// splitter will crop the video to the specified dimensions while tracking the subject.
    /// </summary>
    public (int width, int height)?   Crop                   { get; set; }
    /// <summary>
    /// The fallback point to gravitate towards when tracking the subject. Coordinates are normalized (0.0 to 1.0).
    /// By default , the splitter gravitates towards the center of the frame (0.5, 0.5). 
    /// Setting this allows you to bias the tracking towards a specific area of the frame, 
    /// such as left-center (0.2, 0.5) or top-right (0.8, 0.2). This can be useful for 
    /// videos where the subject tends to be off-center or for creative framing choices.
    /// </summary>
    public Point2f                    GravitateTo            { get; set; } = new Point2f(0.5f, 0.5f);
    /// <summary>
    /// Destination file mask.
    /// </summary>
    public string?                    Mask                   { get; set; }
    /// <summary>
    /// Instead of producing the output, just generate debug frames with tracking 
    /// overlay to visually verify that the tracking is working correctly.
    /// </summary>
    public bool                       Debug                  { get; set; }
    /// <summary>
    /// Type of detector to use for tracking. Supported values are: face (UltraFace), 
    /// body (YoloOnnx, default), none (no tracking, just a center point).
    /// </summary>
    public string?                    Detect                 { get; set; }
    /// <summary>
    /// Detection confidence threshold. This is a value between 0.0 and 1.0 that sets the minimum confidence
    /// </summary>
    public float                      ScoreThreshold         { get; set; } = 0.25f;
    /// <summary>
    /// Identity matching confidence threshold. This is a value between 0.0 and 1.0 that sets the minimum confidence
    /// </summary>
    public float                      IdentityThreshold      { get; set; } = 0.25f;
    /// <summary>
    /// Face or human detectors should only report detections if their upper bound starts below this threshold. 
    /// This is a value between 0.0 and 1.0 mapped to 0..Height.
    /// </summary>
    public float                      DetectAbove            { get; set; } = 0.7f;
    /// <summary>
    /// Object ID to track. This is a hexadecimal string that identifies a specific face or 
    /// person to track across segments. This is useful when you want to consistently track the same person
    /// publacross all segments of a video, even if there are multiple people present
    /// The ID can be obtained when running with --debug or from the debug overlay.
    /// </summary>
    public ulong?                     DetectId               { get; set; }
    /// <summary>
    /// Set starget segments length explicitly. By default, the splitter calculates segment 
    /// lengths to be equal and not exceed 58 seconds.
    /// </summary>
    public double?                    OverrideTargetDuration { get; set; }
    /// <summary>
    /// Parameters to pass thru to ffmpeg. These are specified after "--" in the command 
    /// line and are passed directly to the ffmpeg command line for each segment.
    /// </summary>
    public string[]                   Passthrough            { get; set; } = [];
    /// <summary>
    /// Debugging parameter. Instead of text UI putput lines in plain text. 
    /// This is useful when the output is being piped to a file or another program, 
    /// or when the user prefers a simpler log format without progress bars and dynamic updates.
    /// </summary>
    public bool                       PlainText              { get; set; }
    /// <summary>
    /// Debugging parameter. Just show estimated segments length, count, and other info 
    /// without actually performing the splitting.
    /// </summary>
    public bool                       EstimateOnly           { get; set; }
    /// <summary>
    /// Do not adapt segment length. When set, the splitter will use the exact 
    /// segment duration specified by --duration for all segments except possibly 
    /// the last one, which may be shorter.
    /// </summary>
    public bool                       ForceFixed             { get; set; }
    /// <summary>
    /// Use single thread for operations. When set, the splitter will not run 
    /// multiple ffmpeg processes in parallel.
    /// </summary>
    public bool                       SingleThreaded         { get; set; }
    /// <summary>
    /// Rotation angle: 90, 180, or 270 degrees. This is useful for videos that 
    /// have incorrect orientation metadata.
    /// </summary>
    public int?                       Rotate                 { get; set; }
    /// <summary>
    /// Autodetect if rotation is needed. Not very reliable but can work for some videos. 
    /// Uses edge orientation statistics to determine if the video is rotated and 
    /// applies the appropriate rotation if needed.
    /// </summary>
    public bool                       RotateAuto             { get; set; }
    /// <summary>
    /// Override internal parameters. This allows you to set custom parameters for the 
    /// object detector or rotation detector.
    /// </summary>
    public Dictionary<string, string> Parameters             { get; set; } = [];
    /// <summary>
    /// Increase output resolution by x4 using super-resolution RealBasicVSR_x4 model.
    /// </summary>
    public bool                       Enhance                { get; set; }

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

    public void CopyTo(SingleJob target)
    {
        target.OutputFolder           = OutputFolder;
        target.Crop                   = Crop;
        target.GravitateTo            = GravitateTo;
        target.Mask                   = Mask;
        target.Debug                  = Debug;
        target.Detect                 = Detect;
        target.ScoreThreshold         = ScoreThreshold;
        target.IdentityThreshold      = IdentityThreshold;
        target.DetectAbove            = DetectAbove;
        target.DetectId               = DetectId;
        target.OverrideTargetDuration = OverrideTargetDuration;
        target.Passthrough            = Passthrough.ToArray();
        target.PlainText              = PlainText;
        target.EstimateOnly           = EstimateOnly;
        target.ForceFixed             = ForceFixed;
        target.SingleThreaded         = SingleThreaded;
        target.Rotate                 = Rotate;
        target.RotateAuto             = RotateAuto;
        target.Parameters             = new Dictionary<string, string>(Parameters);
        target.Enhance                = Enhance;
    }

    public void CopyFrom(SingleJob source) => source.CopyTo(this);
}

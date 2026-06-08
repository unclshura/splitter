namespace Splitter_UI.Models;

public class PreviewData
{
    public Avalonia.Media.Imaging.Bitmap? Frame          { get; }
    public IReadOnlyList<OpenCvSharp.Rect> DetectedBoxes { get; }
    public Rect? CropRect                                { get; }
    public Point2f GravitateTo                           { get; }
    public TimeSpan Position                             { get; }
    public int? Rotate                                   { get; }

    public PreviewData(Avalonia.Media.Imaging.Bitmap? frame, IReadOnlyList<OpenCvSharp.Rect> boxes, Rect? crop, Point2f gravitateTo, TimeSpan position, int? rotate)
    {
        Frame         = frame;
        DetectedBoxes = boxes;
        CropRect      = crop;
        GravitateTo   = gravitateTo;
        Position      = position;
        Rotate        = rotate;
    }

}
using Avalonia;

namespace Splitter_UI.Models;

public class PreviewData
{
    public Avalonia.Media.Imaging.Bitmap? Frame { get; }
    public IReadOnlyList<Rect> DetectedBoxes { get; }
    public Rect? CropRect { get; }

    public PreviewData(Avalonia.Media.Imaging.Bitmap? frame, IReadOnlyList<Rect> boxes, Rect? crop)
    {
        Frame = frame;
        DetectedBoxes = boxes;
        CropRect = crop;
    }

}
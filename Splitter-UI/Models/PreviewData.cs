using Avalonia;

namespace Splitter_UI.Models;

public sealed class PreviewData
{
    public Avalonia.Media.Imaging.Bitmap? Frame { get; init; }
    public IReadOnlyList<Rect> FaceBoxes { get; init; } = [];
    public IReadOnlyList<Rect> BodyBoxes { get; init; } = [];
    public Rect? CropRect { get; init; }
}

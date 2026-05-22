using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Splitter_UI.Views;

public sealed class PreviewCanvas : Control
{
    public static readonly StyledProperty<PreviewData?> PreviewProperty =
        AvaloniaProperty.Register<PreviewCanvas, PreviewData?>(nameof(Preview));

    public PreviewData? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    static PreviewCanvas()
    {
        PreviewProperty.Changed.AddClassHandler<PreviewCanvas>(
            (canvas, args) =>
                canvas.OnPreviewChanged(args.OldValue as PreviewData,
                                        args.NewValue as PreviewData));
    }

    private void OnPreviewChanged(PreviewData? oldValue, PreviewData? newValue)
    {
        if (oldValue is INotifyPropertyChanged oldNotify)
            oldNotify.PropertyChanged -= PreviewPropertyChanged;

        if (newValue is INotifyPropertyChanged newNotify)
            newNotify.PropertyChanged += PreviewPropertyChanged;

        // Always marshal to UI thread
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private void PreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewData.Frame) ||
            e.PropertyName == nameof(PreviewData.DetectedBoxes) ||
            e.PropertyName == nameof(PreviewData.CropRect))
        {
            // Always marshal to UI thread
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public override void Render(DrawingContext context)
    {
        var preview = Preview;
        if (preview?.Frame is null)
            return;
        
        var frame = preview.Frame;
        var rawW = frame.PixelSize.Width;
        var rawH = frame.PixelSize.Height;

        var dispW = Bounds.Width;
        var dispH = Bounds.Height;

        if (dispW <= 0 || dispH <= 0)
            return;

        var scale = Math.Min(dispW / rawW, dispH / rawH);

        var scaledW = rawW * scale;
        var scaledH = rawH * scale;

        var offsetX = (dispW - scaledW) / 2;
        var offsetY = (dispH - scaledH) / 2;

        // draw frame
        context.DrawImage(frame,
            new Rect(0, 0, rawW, rawH),
            new Rect(offsetX, offsetY, scaledW, scaledH));

        // draw overlays
        if (preview.DetectedBoxes  is { Count: > 0 })
        {
            var pen = new Pen(Brushes.Lime, 2);

            foreach (var r in preview.DetectedBoxes )
            {
                var rr = new Rect(
                    offsetX + r.X * scale,
                    offsetY + r.Y * scale,
                    r.Width * scale,
                    r.Height * scale);

                context.DrawRectangle(null, pen, rr);
            }
        }
    }
}

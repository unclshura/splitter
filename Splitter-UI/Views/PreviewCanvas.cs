using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using splitter.algo;

namespace Splitter_UI.Views;

public sealed class PreviewCanvas : Control
{
    public static readonly StyledProperty<PreviewData?> PreviewProperty =
        AvaloniaProperty.Register<PreviewCanvas, PreviewData?>(nameof(Preview));
    public static readonly StyledProperty<Point2f?> SarProperty =
        AvaloniaProperty.Register<PreviewCanvas, Point2f?>(nameof(Sar));
    public static readonly StyledProperty<int> RotateAngleProperty =
        AvaloniaProperty.Register<PreviewCanvas, int>(nameof(RotateAngle));

    public PreviewData? Preview
    {
        get => GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public Point2f? Sar
    {
        get => GetValue(SarProperty);
        set => SetValue(SarProperty, value);
    }

    public int RotateAngle
    {
        get => GetValue(RotateAngleProperty);
        set => SetValue(RotateAngleProperty, value);
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

        var rotate = RotateAngle; // 0, 90, 180, 270

        // SAR (always original, never rotated)
        var sar = Sar ?? new Point2f(1, 1);
        var sarX = sar.X;
        var sarY = sar.Y;

        if (sarX <= 0 || sarY <= 0)
        {
            sarX = 1;
            sarY = 1;
        }

        var pixelAspect = sarX / sarY;

        double displayW;
        double displayH;

        if (rotate == 0 || rotate == 180)
        {
            // encoded horizontal axis = rawW
            displayW = rawW * pixelAspect;
            displayH = rawH;
        }
        else
        {
            // encoded horizontal axis = rawH (bitmap already rotated)
            displayW = rawW;
            displayH = rawH * pixelAspect;
        }

        var scale = Math.Min(dispW / displayW, dispH / displayH);

        var scaledW = displayW * scale;
        var scaledH = displayH * scale;

        var offsetX = (dispW - scaledW) / 2;
        var offsetY = (dispH - scaledH) / 2;

        // draw frame
        context.DrawImage(
            frame,
            new Rect(0, 0, rawW, rawH),
            new Rect(offsetX, offsetY, scaledW, scaledH));

        // overlays
        if (preview.DetectedBoxes is { Count: > 0 })
        {
            var pen = new Pen(Brushes.Lime, 2);

            foreach (var r in preview.DetectedBoxes)
            {
                double x = r.X;
                double y = r.Y;
                double w = r.Width;
                double h = r.Height;

                // rotate overlay coordinates (still using your existing logic)
                switch (rotate)
                {
                    case 90:
                        (x, y) = (rawH - (y + h), x);
                        (w, h) = (h, w);
                        break;

                    case 180:
                        x = rawW - (x + w);
                        y = rawH - (y + h);
                        break;

                    case 270:
                        (x, y) = (y, rawW - (x + w));
                        (w, h) = (h, w);
                        break;
                }

                // apply SAR to the axis that originated from encoded width
                if (rotate == 0 || rotate == 180)
                {
                    x *= pixelAspect;
                    w *= pixelAspect;
                }
                else
                {
                    y *= pixelAspect;
                    h *= pixelAspect;
                }

                var rr = new Rect(
                offsetX + x * scale,
                offsetY + y * scale,
                w * scale,
                h * scale);

                context.DrawRectangle(null, pen, rr);
            }
        }
    }

}

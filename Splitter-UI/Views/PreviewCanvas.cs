using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Splitter_UI.Views;

public sealed class PreviewCanvas : Control
{
    public static readonly StyledProperty<PreviewData?> PreviewProperty =
        AvaloniaProperty.Register<PreviewCanvas, PreviewData?>(nameof(Preview));
    public static readonly StyledProperty<Point2f?> SarProperty =
        AvaloniaProperty.Register<PreviewCanvas, Point2f?>(nameof(Sar));
    public static readonly StyledProperty<int> RotateAngleProperty =
        AvaloniaProperty.Register<PreviewCanvas, int>(nameof(RotateAngle));
    public static readonly StyledProperty<Point2f> GravitateToProperty =
        AvaloniaProperty.Register<PreviewCanvas, Point2f>(nameof(GravitateTo));

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

    // GravitateTo is normalized (0..1)
    public Point2f GravitateTo
    {
        get => GetValue(GravitateToProperty);
        set => SetValue(GravitateToProperty, value);
    }

    private bool           _dragging;
    private Avalonia.Point _dragStartCanvas;
    private Point2f        _dragStartValue;

    static PreviewCanvas()
    {
        PreviewProperty.Changed.AddClassHandler<PreviewCanvas>(
            (canvas, args) =>
                canvas.OnPreviewChanged(args.OldValue as PreviewData,
                                        args.NewValue as PreviewData));
    }

    public PreviewCanvas()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    private void OnPreviewChanged(PreviewData? oldValue, PreviewData? newValue)
    {
        if (oldValue is INotifyPropertyChanged oldNotify)
            oldNotify.PropertyChanged -= PreviewPropertyChanged;

        if (newValue is INotifyPropertyChanged newNotify)
            newNotify.PropertyChanged += PreviewPropertyChanged;

        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private void PreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewData.Frame) ||
            e.PropertyName == nameof(PreviewData.DetectedBoxes) ||
            e.PropertyName == nameof(PreviewData.CropRect))
        {
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    // ------------------------------------------------------------
    // Unified transform helpers
    // ------------------------------------------------------------

    private (double X, double Y) TransformPoint(
        double x, double y,
        double rawW, double rawH,
        double offsetX, double offsetY,
        double scale,
        int rotate,
        double pixelAspect)
    {
        switch (rotate)
        {
            case 90:
                (x, y) = (rawH - y, x);
                break;
            case 180:
                x = rawW - x;
                y = rawH - y;
                break;
            case 270:
                (x, y) = (y, rawW - x);
                break;
        }

        if (rotate == 0 || rotate == 180)
            x *= pixelAspect;
        else
            y *= pixelAspect;

        var sx = offsetX + x * scale;
        var sy = offsetY + y * scale;

        return (sx, sy);
    }

    private Rect TransformRect(
        double x, double y, double w, double h,
        double rawW, double rawH,
        double offsetX, double offsetY,
        double scale,
        int rotate,
        double pixelAspect)
    {
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

        return new Rect(
            offsetX + x * scale,
            offsetY + y * scale,
            w * scale,
            h * scale);
    }

    // ------------------------------------------------------------
    // Hit test for gravitate point (normalized)
    // ------------------------------------------------------------

    private bool HitGravitate(Avalonia.Point p, out Point2f value)
    {
        value = default;

        var preview = Preview;
        if (preview?.Frame is null)
            return false;

        var g = GravitateTo;

        var rawW = preview.Frame.PixelSize.Width;
        var rawH = preview.Frame.PixelSize.Height;

        // normalized → pixel
        double px = g.X * rawW;
        double py = g.Y * rawH;

        var rotate = RotateAngle;
        var sar = Sar ?? new Point2f(1, 1);
        var pixelAspect = sar.X / sar.Y;

        var dispW = Bounds.Width;
        var dispH = Bounds.Height;

        double displayW, displayH;
        if (rotate == 0 || rotate == 180)
        {
            displayW = rawW * pixelAspect;
            displayH = rawH;
        }
        else
        {
            displayW = rawW;
            displayH = rawH * pixelAspect;
        }

        var scale = Math.Min(dispW / displayW, dispH / displayH);
        var offsetX = (dispW - displayW * scale) / 2;
        var offsetY = (dispH - displayH * scale) / 2;

        var (cx, cy) = TransformPoint(
            px, py,
            rawW, rawH,
            offsetX, offsetY,
            scale,
            rotate,
            pixelAspect);

        const double radius = 10;
        var hit = (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy) <= radius * radius;

        if (hit)
            value = g;

        return hit;
    }

    // ------------------------------------------------------------
    // Pointer events
    // ------------------------------------------------------------

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);

        if (HitGravitate(p, out var g))
        {
            _dragging = true;
            _dragStartCanvas = p;
            _dragStartValue = g; // normalized
            e.Pointer.Capture(this);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || Preview?.Frame is null)
            return;

        var p = e.GetPosition(this);
        var dxCanvas = p.X - _dragStartCanvas.X;
        var dyCanvas = p.Y - _dragStartCanvas.Y;

        var preview = Preview;
        var rawW = preview.Frame.PixelSize.Width;
        var rawH = preview.Frame.PixelSize.Height;

        var rotate = RotateAngle;
        var sar = Sar ?? new Point2f(1, 1);
        var pixelAspect = sar.X / sar.Y;

        var dispW = Bounds.Width;
        var dispH = Bounds.Height;

        double displayW, displayH;
        if (rotate == 0 || rotate == 180)
        {
            displayW = rawW * pixelAspect;
            displayH = rawH;
        }
        else
        {
            displayW = rawW;
            displayH = rawH * pixelAspect;
        }

        var scale = Math.Min(dispW / displayW, dispH / displayH);

        var dx = dxCanvas / scale;
        var dy = dyCanvas / scale;

        if (rotate == 0 || rotate == 180)
            dx /= pixelAspect;
        else
            dy /= pixelAspect;

        // start normalized → pixel
        var gx = _dragStartValue.X * rawW + dx;
        var gy = _dragStartValue.Y * rawH + dy;

        switch (rotate)
        {
            case 90:
                (gx, gy) = (gy, rawH - gx);
                break;
            case 180:
                gx = rawW - gx;
                gy = rawH - gy;
                break;
            case 270:
                (gx, gy) = (rawW - gy, gx);
                break;
        }

        // pixel → normalized
        var nx = (float)(gx / rawW);
        var ny = (float)(gy / rawH);

        if (nx < 0) nx = 0;
        if (ny < 0) ny = 0;
        if (nx > 1) nx = 1;
        if (ny > 1) ny = 1;

        GravitateTo = new Point2f(nx, ny);

        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }
    }

    // ------------------------------------------------------------
    // Overlay renderers
    // ------------------------------------------------------------

    private void RenderCropRectangle(
        DrawingContext context,
        PreviewData preview,
        double rawW, double rawH,
        double offsetX, double offsetY,
        double scale,
        int rotate,
        double pixelAspect)
    {
        if (preview.CropRect is not { } crop)
            return;

        var rr = TransformRect(
            crop.X, crop.Y, crop.Width, crop.Height,
            rawW, rawH,
            offsetX, offsetY,
            scale,
            rotate,
            pixelAspect);

        var pen = new Pen(Brushes.Yellow, 2);
        context.DrawRectangle(null, pen, rr);
    }

    private void RenderGravitateTo(
        DrawingContext context,
        PreviewData preview,
        double rawW, double rawH,
        double offsetX, double offsetY,
        double scale,
        int rotate,
        double pixelAspect)
    {
        var g = GravitateTo;

        // normalized → pixel
        var px = g.X * rawW;
        var py = g.Y * rawH;

        var (sx, sy) = TransformPoint(
            px, py,
            rawW, rawH,
            offsetX, offsetY,
            scale,
            rotate,
            pixelAspect);

        const double radius = 10;

        var circle = new EllipseGeometry(
            new Rect(sx - radius, sy - radius, radius * 2, radius * 2));

        var pen = new Pen(Brushes.Yellow, 2);
        var brush = Brushes.Yellow;

        context.DrawGeometry(brush, pen, circle);
    }

    private void RenderDetectedBoxes(
        DrawingContext context,
        PreviewData preview,
        double rawW, double rawH,
        double offsetX, double offsetY,
        double scale,
        int rotate,
        double pixelAspect)
    {
        if (preview.DetectedBoxes is not { Count: > 0 })
            return;

        var pen = new Pen(Brushes.Lime, 2);

        foreach (var r in preview.DetectedBoxes)
        {
            var rr = TransformRect(
                r.X, r.Y, r.Width, r.Height,
                rawW, rawH,
                offsetX, offsetY,
                scale,
                rotate,
                pixelAspect);

            context.DrawRectangle(null, pen, rr);
        }
    }

    // ------------------------------------------------------------
    // Main Render
    // ------------------------------------------------------------

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

        var rotate = RotateAngle;

        var sar = Sar ?? new Point2f(1, 1);
        var sarX = sar.X <= 0 ? 1 : sar.X;
        var sarY = sar.Y <= 0 ? 1 : sar.Y;
        var pixelAspect = sarX / sarY;

        double displayW, displayH;

        if (rotate == 0 || rotate == 180)
        {
            displayW = rawW * pixelAspect;
            displayH = rawH;
        }
        else
        {
            displayW = rawW;
            displayH = rawH * pixelAspect;
        }

        var scale = Math.Min(dispW / displayW, dispH / displayH);

        var scaledW = displayW * scale;
        var scaledH = displayH * scale;

        var offsetX = (dispW - scaledW) / 2;
        var offsetY = (dispH - scaledH) / 2;

        context.DrawImage(
            frame,
            new Rect(0, 0, rawW, rawH),
            new Rect(offsetX, offsetY, scaledW, scaledH));

        RenderDetectedBoxes(context, preview, rawW, rawH, offsetX, offsetY, scale, rotate, pixelAspect);
        RenderCropRectangle(context, preview, rawW, rawH, offsetX, offsetY, scale, rotate, pixelAspect);
        RenderGravitateTo(context, preview, rawW, rawH, offsetX, offsetY, scale, rotate, pixelAspect);
    }
}

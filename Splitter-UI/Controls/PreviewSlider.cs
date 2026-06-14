using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Point = Avalonia.Point;

namespace Splitter_UI.Controls
{
    public sealed class PreviewSlider : Control
    {
        public static readonly StyledProperty<double> MinimumProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(Minimum), 0d);

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(Maximum), 100d);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(
                nameof(Value), 0d,
                coerce: (o, v) =>
                {
                    var slider = (PreviewSlider)o;
                    if (v < slider.Minimum) return slider.Minimum;
                    if (v > slider.Maximum) return slider.Maximum;
                    return v;
                });

        public static readonly StyledProperty<double> SegmentDurationProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(SegmentDuration), 1d);

        public static readonly StyledProperty<double> TrackThicknessProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(TrackThickness), 4d);

        public static readonly StyledProperty<double> ThumbRadiusProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(ThumbRadius), 8d);

        public static readonly StyledProperty<IBrush> TrackBrushProperty =
            AvaloniaProperty.Register<PreviewSlider, IBrush>(nameof(TrackBrush), Brushes.Gray);

        public static readonly StyledProperty<IBrush> TrackFillBrushProperty =
            AvaloniaProperty.Register<PreviewSlider, IBrush>(nameof(TrackFillBrush), Brushes.DodgerBlue);

        public static readonly StyledProperty<IBrush> ThumbBrushProperty =
            AvaloniaProperty.Register<PreviewSlider, IBrush>(nameof(ThumbBrush), Brushes.White);

        public static readonly StyledProperty<IBrush> ThumbBorderBrushProperty =
            AvaloniaProperty.Register<PreviewSlider, IBrush>(nameof(ThumbBorderBrush), Brushes.DodgerBlue);

        public static readonly StyledProperty<double> ThumbBorderThicknessProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(ThumbBorderThickness), 1d);

        public static readonly StyledProperty<IBrush> SegmentLineBrushProperty =
            AvaloniaProperty.Register<PreviewSlider, IBrush>(nameof(SegmentLineBrush), Brushes.LightSalmon);

        public static readonly StyledProperty<double> SegmentLineThicknessProperty =
            AvaloniaProperty.Register<PreviewSlider, double>(nameof(SegmentLineThickness), 1d);

        private bool _isDragging;

        public double Minimum
        {
            get => GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public double Maximum
        {
            get => GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double SegmentDuration
        {
            get => GetValue(SegmentDurationProperty);
            set => SetValue(SegmentDurationProperty, value);
        }

        public double TrackThickness
        {
            get => GetValue(TrackThicknessProperty);
            set => SetValue(TrackThicknessProperty, value);
        }

        public double ThumbRadius
        {
            get => GetValue(ThumbRadiusProperty);
            set => SetValue(ThumbRadiusProperty, value);
        }

        public IBrush TrackBrush
        {
            get => GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public IBrush TrackFillBrush
        {
            get => GetValue(TrackFillBrushProperty);
            set => SetValue(TrackFillBrushProperty, value);
        }

        public IBrush ThumbBrush
        {
            get => GetValue(ThumbBrushProperty);
            set => SetValue(ThumbBrushProperty, value);
        }

        public IBrush ThumbBorderBrush
        {
            get => GetValue(ThumbBorderBrushProperty);
            set => SetValue(ThumbBorderBrushProperty, value);
        }

        public double ThumbBorderThickness
        {
            get => GetValue(ThumbBorderThicknessProperty);
            set => SetValue(ThumbBorderThicknessProperty, value);
        }

        public IBrush SegmentLineBrush
        {
            get => GetValue(SegmentLineBrushProperty);
            set => SetValue(SegmentLineBrushProperty, value);
        }

        public double SegmentLineThickness
        {
            get => GetValue(SegmentLineThicknessProperty);
            set => SetValue(SegmentLineThicknessProperty, value);
        }

        static PreviewSlider()
        {
            FocusableProperty.OverrideDefaultValue<PreviewSlider>(true);

            ValueProperty.Changed.AddClassHandler<PreviewSlider>((s, _) => s.InvalidateVisual());
            MinimumProperty.Changed.AddClassHandler<PreviewSlider>((s, _) => s.InvalidateVisual());
            MaximumProperty.Changed.AddClassHandler<PreviewSlider>((s, _) => s.InvalidateVisual());
            SegmentDurationProperty.Changed.AddClassHandler<PreviewSlider>((s, _) => s.InvalidateVisual());
        }


        public PreviewSlider()
        {
            ClipToBounds = true;

            AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
            AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var bounds = Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var centerY = bounds.Height / 2.0;
            var left = ThumbRadius;
            var right = bounds.Width - ThumbRadius;

            var trackThickness = TrackThickness;
            var trackRect = new Rect(left, centerY - trackThickness / 2.0, right - left, trackThickness);

            context.FillRectangle(TrackBrush, trackRect);

            var range = Maximum - Minimum;
            if (SegmentDuration > 0 && range > 0 && SegmentLineBrush != null && SegmentLineThickness > 0)
            {
                var pen = new Pen(SegmentLineBrush, SegmentLineThickness);
                var totalSegments = (int)Math.Floor(range / SegmentDuration);

                for (var i = 1; i <= totalSegments; i++)
                {
                    var segmentValue = Minimum + i * SegmentDuration;
                    var tSeg = (segmentValue - Minimum) / range;
                    var xSeg = left + tSeg * (right - left);

                    var p1 = new Point(xSeg, centerY - trackThickness);
                    var p2 = new Point(xSeg, centerY + trackThickness);
                    context.DrawLine(pen, p1, p2);
                }
            }

            var t = (range <= 0) ? 0.0 : (Value - Minimum) / range;
            t = Math.Clamp(t, 0.0, 1.0);

            var thumbX = left + t * (right - left);

            var fillRect = new Rect(left, centerY - trackThickness / 2.0, thumbX - left, trackThickness);
            context.FillRectangle(TrackFillBrush, fillRect);

            var thumbRadius = ThumbRadius;
            var thumbCenter = new Point(thumbX, centerY);

            var ellipse = new EllipseGeometry(new Rect(
                thumbCenter.X - thumbRadius,
                thumbCenter.Y - thumbRadius,
                thumbRadius * 2,
                thumbRadius * 2));

            context.DrawGeometry(ThumbBrush, null, ellipse);

            if (ThumbBorderThickness > 0 && ThumbBorderBrush != null)
            {
                var pen = new Pen(ThumbBorderBrush, ThumbBorderThickness);
                context.DrawGeometry(null, pen, ellipse);
            }

        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            var delta = e.Delta.Y;
            if (delta == 0)
                return;

            var step = (Maximum - Minimum) / 100.0;
            if (step <= 0)
                step = 1.0;

            if (delta > 0)
                Value = Math.Clamp(Value - step, Minimum, Maximum);
            else
                Value = Math.Clamp(Value + step, Minimum, Maximum);

            e.Handled = true;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsEnabled)
                return;

            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            e.Pointer.Capture(this);
            UpdateValueFromPoint(e.GetPosition(this));
            _isDragging = true;
            e.Handled = true;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging)
                return;

            UpdateValueFromPoint(e.GetPosition(this));
            e.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _isDragging = false;
        }

        private void UpdateValueFromPoint(Point point)
        {
            var bounds = Bounds;
            var left = ThumbRadius;
            var right = bounds.Width - ThumbRadius;

            if (right <= left)
                return;

            var x = Math.Clamp(point.X, left, right);
            var t = (x - left) / (right - left);

            var newValue = Minimum + t * (Maximum - Minimum);
            Value = newValue;

            InvalidateVisual();
        }
    }
}

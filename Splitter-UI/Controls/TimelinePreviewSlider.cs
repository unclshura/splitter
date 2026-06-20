using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Point = Avalonia.Point;

namespace Splitter_UI.Controls;

public class TimelinePreviewSlider : Control, IDisposable
{
    // Public properties
    public static readonly StyledProperty<JobViewModel?> ViewModelProperty =
            AvaloniaProperty.Register<TimelinePreviewSlider, JobViewModel?>(nameof(ViewModel));

    public JobViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private double PixelsPerSecond
    {
        get
        {
            var vm = ViewModel;
            if (vm == null || vm.DurationSeconds <= 0 || Bounds.Width <= 0)
                return 10000; // fallback value

            // Full control width maps to full video duration
            return Bounds.Width / vm.DurationSeconds;
        }
    }

    public static readonly StyledProperty<IBrush?> SegmentFillProperty =
            AvaloniaProperty.Register<TimelinePreviewSlider, IBrush?>(nameof(SegmentFill), Brushes.DimGray);

    public IBrush? SegmentFill
    {
        get => GetValue(SegmentFillProperty);
        set => SetValue(SegmentFillProperty, value);
    }

    public static readonly StyledProperty<IBrush?> MarkerStrokeProperty =
            AvaloniaProperty.Register<TimelinePreviewSlider, IBrush?>(nameof(MarkerStroke), Brushes.White);

    public IBrush? MarkerStroke
    {
        get => GetValue(MarkerStrokeProperty);
        set => SetValue(MarkerStrokeProperty, value);
    }

    // Visual constants
    private const double _timelineHeight         = 80;
    private const double _markerLineHeight       = 36;
    private const double _markerLineWidth        = 2;
    private const double _markerTriangleSize     = 8;
    private const double _segmentBarHeight       = 40;
    private const int    _maxPreviewCacheItems   = 128;

    // Internal state
    private readonly LruCache<string, Bitmap> _previewCache = new(_maxPreviewCacheItems);
    private readonly Dictionary<string, CancellationTokenSource> _previewLoadCts = new();
    private readonly object _cacheLock = new();

    private IDisposable?  _segmentsSubscription;
    private bool          _isInternalSliderUpdate;
    private JobViewModel? _currentVm;

    // Interaction state
    private bool         _isPointerCaptured;
    private Point        _lastPointerPoint;
    private DragMode     _dragMode           = DragMode.None;
    private int          _activeSegmentIndex = -1;
    private bool         _isSplitModifierActive;

    // Throttle invalidation during drag
    private DateTime _lastInvalidate = DateTime.MinValue;
    private readonly TimeSpan _invalidateThrottle = TimeSpan.FromMilliseconds(16); // ~60Hz

    public TimelinePreviewSlider()
    {
        Focusable    = true;
        Height       = _timelineHeight;
        ClipToBounds = true;

        // Use property change override instead of GetObservable.Subscribe to avoid IObserver compile issues.
        PointerPressed     += OnPointerPressed;
        PointerMoved       += OnPointerMoved;
        PointerReleased    += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        KeyDown            += OnKeyDown;
        KeyUp              += OnKeyUp;
    }

    // Override to detect ViewModel property changes
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ViewModelProperty)
        {
            OnViewModelChanged((JobViewModel?)change.NewValue);
        }
    }

    private void OnViewModelChanged(JobViewModel? vm)
    {
        UnsubscribeFromViewModel();
        _previewCache.Clear();
        CancelAllPreviewLoads();

        if (vm != null)
        {
            _segmentsSubscription = SubscribeToSegments(vm.Segments);
            vm.PropertyChanged += OnVmPropertyChanged;
        }

        InvalidateVisual();
    }


    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobViewModel.SliderLiveValue))
        {
            if (_isInternalSliderUpdate)
                return;

            InvalidateVisual();
        }
    }

    private IDisposable SubscribeToSegments(ObservableCollection<Segment> segments)
    {
        NotifyCollectionChangedEventHandler handler = (s, e) =>
        {
            Dispatcher.UIThread.Post(() => {
                InvalidateVisual();
            }, DispatcherPriority.Background);
        };
        segments.CollectionChanged += handler;
        return Disposable.Create(() => segments.CollectionChanged -= handler);
    }

    private void UnsubscribeFromViewModel()
    {
        if (_currentVm != null)
            _currentVm.PropertyChanged -= OnVmPropertyChanged;

        _currentVm = null;

        _segmentsSubscription?.Dispose();
        _segmentsSubscription = null;
    }

    private void CancelAllPreviewLoads()
    {
        lock (_cacheLock)
        {
            foreach (var cts in _previewLoadCts.Values)
            {
                try { cts.Cancel(); } catch { }
            }
            _previewLoadCts.Clear();
        }
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);

        var vm = ViewModel;
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);

        // Background
        dc.FillRectangle(Brushes.Black, bounds);

        DrawContinuousPreviewStrip(dc);
        DrawGapOverlays(dc);
        DrawOverlongSegmentOverlays(dc);

        if (vm == null || vm.DurationSeconds <= 0 || vm.Segments.Count == 0)
        {
            // draw empty ruler
            DrawRuler(dc, 0, vm?.DurationSeconds ?? 0);
            return;
        }

        // Draw segments and previews
        for (int i = 0; i < vm.Segments.Count; i++)
        {
            var seg = vm.Segments[i];
            var segRect = SegmentRectFor(seg);
            if (segRect.Width <= 0) continue;

            // Segment background
            var segBrush = new Pen(SegmentFill ?? Brushes.DimGray, 1);
            var segRoundedRect = new Rect(segRect.X, segRect.Y + (Bounds.Height - _segmentBarHeight) / 2, segRect.Width, _segmentBarHeight);
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(segRoundedRect.X, segRoundedRect.Y), true);
                ctx.LineTo(     new Point(segRoundedRect.X + segRoundedRect.Width, segRoundedRect.Y));
                ctx.LineTo(     new Point(segRoundedRect.X + segRoundedRect.Width, segRoundedRect.Y + segRoundedRect.Height));
                ctx.LineTo(     new Point(segRoundedRect.X, segRoundedRect.Y + segRoundedRect.Height));
                ctx.EndFigure(true);
            }
            dc.DrawGeometry(null, segBrush, geom);
        }

        // Draw markers on top
        for (int i = 0; i < vm.Segments.Count; i++)
        {
            var seg = vm.Segments[i];
            DrawMarker(dc, seg.Start, true);
            DrawMarker(dc, seg.End, false);
        }

        // Draw current position indicator
        DrawPositionIndicator(dc, vm.SliderLiveValue);

        // Draw ruler
        DrawRuler(dc, 0, vm.DurationSeconds);
    }

    private void DrawRuler(DrawingContext dc, double startSec, double endSec)
    {
        var height = Bounds.Height;
        var y = height - 18;
        var pen = new Pen(Brushes.Gray, 1);
        dc.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));

        if (ViewModel == null || ViewModel.DurationSeconds <= 0) return;

        var totalSec = ViewModel.DurationSeconds;
        var approxTicks = Math.Max(2, (int)(Bounds.Width / 100));
        var tickSec = Math.Max(1.0, totalSec / approxTicks);

        for (double t = 0; t <= totalSec; t += tickSec)
        {
            var x = SecondsToPixel(t);
            dc.DrawLine(pen, new Point(x, y), new Point(x, y - 6));
            var text = FormatTime(t);
            var textBrush = Brushes.LightGray; // or new SolidColorBrush(Color.Parse("#FFCCCCCC"));
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                12,
                textBrush);

            dc.DrawText(ft, new Point(x + 2, y - 18));
        }
    }

    private void DrawContinuousPreviewStrip(DrawingContext dc)
    {
        var vm = ViewModel;
        if (vm == null || vm.DurationSeconds <= 0)
            return;

        double stripY = (Bounds.Height - _segmentBarHeight) / 2;
        double stripHeight = _segmentBarHeight;

        double currentX = 0;
        double endX = Bounds.Width;

        var bmp = GetPreview(0);
        var noPreviewAvailable = bmp == null;
        if (bmp == null)
        {
            StartPreviewLoad(0);
            return;
        }

        var previewScale = (double)stripHeight / bmp.PixelSize.Height;
        var previewTileWidth = bmp.PixelSize.Width * previewScale;

        using (dc.PushClip(new Rect(0, stripY, Bounds.Width, stripHeight)))
        {
            while (currentX < endX)
            {
                double posSec = PixelToSeconds(currentX);
                if (posSec < 0) posSec = 0;
                if (posSec > vm.DurationSeconds) posSec = vm.DurationSeconds;

                bmp = GetPreview(posSec);

                if (bmp == null)
                {
                    StartPreviewLoad(posSec);

                    // advance by estimated width
                    currentX += previewTileWidth;
                    continue;
                }

                // scale full frame to strip height
                double scale = stripHeight / bmp.PixelSize.Height;
                double tileWidth = bmp.PixelSize.Width * scale;

                var src = new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height);
                var dst = new Rect(currentX, stripY, tileWidth, stripHeight);

                dc.DrawImage(bmp, src, dst);

                currentX += tileWidth;
            }
        }
    }

    private void DrawGapOverlays(DrawingContext dc)
    {
        var vm = ViewModel;
        if (vm == null || vm.Segments.Count == 0)
            return;

        double stripY = (Bounds.Height - _segmentBarHeight) / 2;
        double stripHeight = _segmentBarHeight;

        var gapBrush = new SolidColorBrush(Color.FromArgb(190, 80, 80, 80)); 

        double lastEnd = 0;

        for (int i = 0; i < vm.Segments.Count; i++)
        {
            var seg = vm.Segments[i];

            if (seg.Start > lastEnd)
            {
                double gapLeft = SecondsToPixel(lastEnd);
                double gapRight = SecondsToPixel(seg.Start);
                double w = gapRight - gapLeft;

                if (w > 0)
                    dc.FillRectangle(gapBrush, new Rect(gapLeft, stripY, w, stripHeight));
            }

            lastEnd = seg.End;
        }

        // tail gap
        if (lastEnd < vm.DurationSeconds)
        {
            double gapLeft = SecondsToPixel(lastEnd);
            double gapRight = SecondsToPixel(vm.DurationSeconds);
            double w = gapRight - gapLeft;

            if (w > 0)
                dc.FillRectangle(gapBrush, new Rect(gapLeft, stripY, w, stripHeight));
        }
    }

    private void DrawOverlongSegmentOverlays(DrawingContext dc)
    {
        var vm = ViewModel;
        if (vm == null || vm.Segments.Count == 0)
            return;

        if (vm.OverrideTargetDuration <= 0)
            return;

        double stripY = (Bounds.Height - _segmentBarHeight) / 2;
        double stripHeight = _segmentBarHeight;

        var overBrush = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0)); // 50% red

        foreach (var seg in vm.Segments)
        {
            double length = seg.End - seg.Start;
            if (length <= vm.OverrideTargetDuration)
                continue;

            double left = SecondsToPixel(seg.Start);
            double right = SecondsToPixel(seg.End);
            double w = right - left;

            if (w > 0)
                dc.FillRectangle(overBrush, new Rect(left, stripY, w, stripHeight));
        }
    }

    private Bitmap? GetPreview(double pos)
    {
        var key = PreviewCacheKey(pos);
        lock (_cacheLock)
        {
            _previewCache.TryGet(key, out var bmp);
            return bmp;
        }
    }

    private void StartPreviewLoad(double pos)
    {
        var vm = ViewModel;
        if (vm == null) 
            return;

        var key = PreviewCacheKey(pos);
        lock (_cacheLock)
        {
            if (_previewLoadCts.ContainsKey(key) || _previewCache.ContainsKey(key))
                return;

            var cts = new CancellationTokenSource();
            _previewLoadCts[key] = cts;

            // Run an async loader on threadpool
            Task.Run(async () =>
            {
                try
                {
                    // call host-provided async GetPreview
                    var bmp = await vm.GetThumbnail(pos).ConfigureAwait(false);

                    if (bmp != null && !cts.IsCancellationRequested)
                    {
                        lock (_cacheLock)
                        {
                            _previewCache.Add(key, bmp);
                            _previewLoadCts.Remove(key);
                        }

                        // notify UI thread to redraw
                        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
                    }
                    else
                    {
                        lock (_cacheLock)
                        {
                            _previewLoadCts.Remove(key);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    lock (_cacheLock) { _previewLoadCts.Remove(key); }
                }
                catch
                {
                    lock (_cacheLock) { _previewLoadCts.Remove(key); }
                }
            }, cts.Token);
        }
    }


    private string PreviewCacheKey(double pos) => $"{pos:F3}";

    private Rect SegmentRectFor(Segment seg)
    {
        var left = SecondsToPixel(seg.Start);
        var right = SecondsToPixel(seg.End);
        var y = 0;
        return new Rect(left, y, Math.Max(0, right - left), Bounds.Height);
    }

    private double SecondsToPixel(double seconds)
    {
        return seconds * PixelsPerSecond;
    }

    private double PixelToSeconds(double px)
    {
        return px / PixelsPerSecond;
    }

    private void DrawMarker(DrawingContext dc, double seconds, bool isStart)
    {
        var x = SecondsToPixel(seconds);
        var top = (Bounds.Height - _segmentBarHeight) / 2 - _markerTriangleSize - 2;
        var lineTop = top + _markerTriangleSize + 2;
        var lineBottom = lineTop + _markerLineHeight;

        double midY = top + _markerTriangleSize / 2.0;

        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            if (isStart)
            {
                // segment is to the right -> triangle points right
                var vTop = new Point(x, top);
                var vBottom = new Point(x, top + _markerTriangleSize);
                var point = new Point(x + _markerTriangleSize, midY);

                ctx.BeginFigure(point, true);
                ctx.LineTo(vBottom);
                ctx.LineTo(vTop);
                ctx.EndFigure(true);
            }
            else
            {
                // segment is to the left -> triangle points left
                var vTop = new Point(x, top);
                var vBottom = new Point(x, top + _markerTriangleSize);
                var point = new Point(x - _markerTriangleSize, midY);

                ctx.BeginFigure(point, true);
                ctx.LineTo(vTop);
                ctx.LineTo(vBottom);
                ctx.EndFigure(true);
            }
        }

        dc.DrawGeometry(MarkerStroke ?? Brushes.White, null, tri);

        var pen = new Pen(MarkerStroke ?? Brushes.White, _markerLineWidth);
        dc.DrawLine(pen, new Point(x, lineTop), new Point(x, lineBottom));
    }

    private void DrawPositionIndicator(DrawingContext dc, double seconds)
    {
        var x = SecondsToPixel(seconds);
        var pen = new Pen(Brushes.Red, 1.5);
        dc.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
    }

    private string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    // Interaction

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();

        var p = e.GetPosition(this);
        _lastPointerPoint = p;
        _isSplitModifierActive = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        var hit = HitTestAtPoint(p);
        if (hit.Type == HitType.StartMarker)
        {
            BeginDrag(DragMode.DragStartMarker, hit.SegmentIndex, e);
        }
        else if (hit.Type == HitType.EndMarker)
        {
            BeginDrag(DragMode.DragEndMarker, hit.SegmentIndex, e);
        }
        else
        {
            // any other hit just moves playhead
            SetPlayheadFromPoint(p);
        }

        if (_isSplitModifierActive && hit.Type == HitType.SegmentBody)
        {
            var sec = PixelToSeconds(p.X);
            TrySplitSegmentAt(hit.SegmentIndex, sec);
        }

        e.Pointer.Capture(this);
        _isPointerCaptured = true;
        e.Handled = true;
    }


    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerCaptured) return;
        var p = e.GetPosition(this);

        if (_dragMode == DragMode.None)
        {
            _lastPointerPoint = p;
            return;
        }

        var vm = ViewModel;
        if (vm == null) return;

        var sec = PixelToSeconds(p.X);
        sec = Math.Max(0, Math.Min(vm.DurationSeconds, sec));

        switch (_dragMode)
        {
            case DragMode.DragStartMarker:
                MoveSegmentStart(_activeSegmentIndex, sec);
                break;
            case DragMode.DragEndMarker:
                MoveSegmentEnd(_activeSegmentIndex, sec);
                break;
        }

        _isInternalSliderUpdate = true;
        vm.SliderLiveValue = sec;
        _isInternalSliderUpdate = false;

        ThrottledInvalidate();
        _lastPointerPoint = p;
        e.Handled = true;
    }


    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isPointerCaptured)
        {
            e.Pointer.Capture(null);
            _isPointerCaptured = false;
        }
        _dragMode = DragMode.None;
        _activeSegmentIndex = -1;
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPointerCaptured = false;
        _dragMode = DragMode.None;
        _activeSegmentIndex = -1;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
        {
            _isSplitModifierActive = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            TryDeleteCurrentSegment();
            e.Handled = true;
            return;
        }
    }


    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) 
            _isSplitModifierActive = false;
    }

    private void TryDeleteCurrentSegment()
    {
        var vm = ViewModel;
        if (vm == null)
            return;

        double pos = vm.SliderLiveValue;

        int idx = -1;
        for (int i = 0; i < vm.Segments.Count; i++)
        {
            var s = vm.Segments[i];
            if (pos >= s.Start && pos <= s.End)
            {
                idx = i;
                break;
            }
        }

        if (idx == -1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (idx >= 0 && idx < vm.Segments.Count)
                vm.Segments.RemoveAt(idx);

            if (vm.Segments.Count == 0)
                vm.GenerateSegments();

        }, DispatcherPriority.Background);
    }


    private void BeginDrag(DragMode mode, int segmentIndex, PointerPressedEventArgs e)
    {
        _dragMode = mode;
        _activeSegmentIndex = segmentIndex;
        _lastPointerPoint = e.GetPosition(this);
    }

    private void ThrottledInvalidate()
    {
        var now = DateTime.UtcNow;
        if (now - _lastInvalidate > _invalidateThrottle)
        {
            _lastInvalidate = now;
            InvalidateVisual();
        }
    }

    private void SetPlayheadFromPoint(Point p)
    {
        var vm = ViewModel;
        if (vm == null) 
            return;

        double sec = PixelToSeconds(p.X);
        sec = Math.Max(0, Math.Min(vm.DurationSeconds, sec));

        _isInternalSliderUpdate = true;
        vm.SliderLiveValue = sec;
        _isInternalSliderUpdate = false;

        InvalidateVisual();
    }

    private void MoveSegmentStart(int index, double newStart)
    {
        var vm = ViewModel;
        if (vm == null) return;
        if (index < 0 || index >= vm.Segments.Count) return;

        var seg = vm.Segments[index];
        var min = index == 0 ? 0.0 : vm.Segments[index - 1].End;
        var max = seg.End - 0.001;
        var clamped = Math.Max(min, Math.Min(max, newStart));
        if (Math.Abs(clamped - seg.Start) < 1e-6) return;

        var newSeg = seg with { Start = clamped };
        vm.Segments[index] = newSeg;
    }

    private void MoveSegmentEnd(int index, double newEnd)
    {
        var vm = ViewModel;
        if (vm == null) return;
        if (index < 0 || index >= vm.Segments.Count) return;

        var seg = vm.Segments[index];
        var min = seg.Start + 0.001;
        var max = index == vm.Segments.Count - 1 ? vm.DurationSeconds : vm.Segments[index + 1].Start;
        var clamped = Math.Max(min, Math.Min(max, newEnd));
        if (Math.Abs(clamped - seg.End) < 1e-6) return;

        var newSeg = seg with { End = clamped };
        vm.Segments[index] = newSeg;
    }

    private void MoveSegmentByDelta(int index, double deltaSec)
    {
        var vm = ViewModel;
        if (vm == null) return;
        if (index < 0 || index >= vm.Segments.Count) return;

        var seg = vm.Segments[index];
        var leftLimit = index == 0 ? 0.0 : vm.Segments[index - 1].End;
        var rightLimit = index == vm.Segments.Count - 1 ? vm.DurationSeconds : vm.Segments[index + 1].Start;

        var newStart = seg.Start + deltaSec;
        var newEnd = seg.End + deltaSec;

        // clamp so segment stays within neighbors
        var segLength = seg.End - seg.Start;
        if (newStart < leftLimit)
        {
            newStart = leftLimit;
            newEnd = newStart + segLength;
        }
        if (newEnd > rightLimit)
        {
            newEnd = rightLimit;
            newStart = newEnd - segLength;
        }

        // apply
        vm.Segments[index] = seg with { Start = newStart, End = newEnd };
    }

    private void TrySplitSegmentAt(int index, double sec)
    {
        var vm = ViewModel;
        if (vm == null) return;
        if (index < 0 || index >= vm.Segments.Count) return;

        var seg = vm.Segments[index];
        if (sec <= seg.Start + 0.001 || sec >= seg.End - 0.001) return;

        var left = seg with { End = sec };
        var right = seg with { Start = sec };
        vm.Segments[index] = left;
        vm.Segments.Insert(index + 1, right);
        InvalidateVisual();
    }

    private HitResult HitTestAtPoint(Point p)
    {
        var vm = ViewModel;
        if (vm == null)
            return new HitResult(HitType.None, -1);

        double topRegion = Bounds.Height / 4.0;

        for (int i = 0; i < vm.Segments.Count; i++)
        {
            var seg = vm.Segments[i];
            double startX = SecondsToPixel(seg.Start);
            double endX   = SecondsToPixel(seg.End);

            // marker hit only in top 1/4
            if (p.Y >= 0 && p.Y <= topRegion)
            {
                // start marker triangle footprint: [startX .. startX + size]
                if (p.X >= startX && p.X <= startX + _markerTriangleSize)
                    return new HitResult(HitType.StartMarker, i);

                // end marker triangle footprint: [endX - size .. endX]
                if (p.X >= endX - _markerTriangleSize && p.X <= endX)
                    return new HitResult(HitType.EndMarker, i);
            }

            // segment body (no drag, only click/split)
            if (p.X >= startX && p.X <= endX && p.Y >= 0 && p.Y <= Bounds.Height)
                return new HitResult(HitType.SegmentBody, i);
        }

        return new HitResult(HitType.Gap, -1);
    }

    // IDisposable
    public void Dispose()
    {
        UnsubscribeFromViewModel();
        CancelAllPreviewLoads();
        _previewCache.Clear();
    }

    // Helpers and small types

    private enum DragMode
    {
        None,
        DragStartMarker,
        DragEndMarker
    }

    private enum HitType
    {
        None,
        StartMarker,
        EndMarker,
        SegmentBody,
        Gap
    }

    private readonly struct HitResult
    {
        public HitType Type { get; }
        public int SegmentIndex { get; }
        public HitResult(HitType type, int idx) { Type = type; SegmentIndex = idx; }
    }

    // Simple LRU cache for Bitmaps
    private class LruCache<TKey, TValue> where TKey : notnull where TValue : class
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TValue value)>> _map;
        private readonly LinkedList<(TKey key, TValue value)> _list;
        private readonly object _sync = new();

        public LruCache(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>();
            _list = new LinkedList<(TKey, TValue)>();
        }

        public bool TryGet(TKey key, out TValue? value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    value = node.Value.value;
                    _list.Remove(node);
                    _list.AddFirst(node);
                    return true;
                }
                value = null;
                return false;
            }
        }

        public void Add(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _list.Remove(node);
                    _map.Remove(key);
                }

                var newNode = new LinkedListNode<(TKey, TValue)>((key, value));
                _list.AddFirst(newNode);
                _map[key] = newNode;

                if (_map.Count > _capacity)
                {
                    var last = _list.Last!;
                    _map.Remove(last.Value.key);
                    _list.RemoveLast();
                    if (last.Value.value is IDisposable d)
                    {
                        try { d.Dispose(); } catch { }
                    }
                }
            }
        }

        public bool ContainsKey(TKey key)
        {
            lock (_sync) return _map.ContainsKey(key);
        }

        public void Clear()
        {
            lock (_sync)
            {
                foreach (var node in _list)
                {
                    if (node.value is IDisposable d)
                    {
                        try { d.Dispose(); } catch { }
                    }
                }
                _map.Clear();
                _list.Clear();
            }
        }
    }

    // Disposable helper
    private static class Disposable
    {
        public static IDisposable Create(Action dispose)
        {
            return new AnonymousDisposable(dispose);
        }

        private sealed class AnonymousDisposable : IDisposable
        {
            private Action? _dispose;
            public AnonymousDisposable(Action dispose) { _dispose = dispose; }
            public void Dispose() { var d = Interlocked.Exchange(ref _dispose, null); d?.Invoke(); }
        }
    }
}
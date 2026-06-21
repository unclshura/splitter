using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class JobViewModel : ObservableObject
{
    private SingleJob Job => _f.Model;
    private ViewModelForwarder<SingleJob> _f;

    public SingleJob GetJob() => Job;

    [ObservableProperty] private VideoInfo?    _probe;
    [ObservableProperty] private PreviewData?  _preview = new(null, [], null, new(0.5f, 0.5f), TimeSpan.Zero, null);
    [ObservableProperty] private Bitmap?       _thumbnail;
    [ObservableProperty] private double        _sliderLiveValue;
    [ObservableProperty] private double        _positionSeconds;

    public string InputFile       => Job.InputFile;
    public double DurationSeconds => Probe?.Duration ?? 0;
    public double SegmentDuration
    {
        get
        {
            if (Probe == null || Probe.Duration <= 0)
                return 58.0;

            var target = Job.OverrideTargetDuration ?? 58.0;

            int segments;
            double segmentLength;

            if (Job.ForceFixed)
            {
                // Fixed chunk size, last one may be shorter
                segments = (int)Math.Ceiling(Probe.Duration / target);
                segmentLength = target;
            }
            else
            {
                // Equalized segments
                segments = (int)Math.Ceiling(Probe.Duration / target);
                segmentLength = Probe.Duration / segments;
            }

            return segmentLength;
        }
    }

    public IRelayCommand StepForwardCommand  { get; }
    public IRelayCommand StepBackwardCommand { get; }
    public IRelayCommand PlayPreviewCommand  { get; }

    private readonly IThumbnailService             _thumbnails;
    private readonly DispatcherTimer               _debounceTimer;
    private readonly Func<string, IObjectTracker>  _trackerFactory;
    private readonly ILogger                       _log;

    public string FileName => Path.GetFileName(Job.InputFile);

    public string TextDesc => Probe != null 
        ? $"{Probe.Width}x{Probe.Height}, {TimeSpan.FromSeconds(Probe.Duration).ToString(@"hh\:mm\:ss")}), FPS: {Probe.Fps:F2}, Bitrate: {Probe.Bitrate/1024/1024:F2} MB/s"
        : "";

    public override string ToString() => $"{FileName}: {TextDesc}";

    public ObservableCollection<ParameterEntry> ParametersList { get; }
        = new();

    public ObservableCollection<Segment> Segments { get; } = new();

    public string CropText
    {
        get => Job.Crop is { } c ? $"{c.width},{c.height}" : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Job.Crop = null;
            }
            else
            {
                var parts = value.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var w) &&
                    int.TryParse(parts[1], out var h))
                    Job.Crop = (w, h);
            }
            OnPropertyChanged();
        }
    }

    public string GravitateText
    {
        get => Job.GravitateTo is { } p ? $"{p.X:F3},{p.Y:F3}" : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Job.GravitateTo = new Point2f(0.5f, 0.5f);
            }
            else
            {
                var parts = value.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], out var x) &&
                    float.TryParse(parts[1], out var y))
                    Job.GravitateTo = new Point2f(x, y);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(GravitateTo));
        }
    }

    public string PassthroughText
    {
        get => string.Join(' ', Job.Passthrough);
        set
        {
            Job.Passthrough = string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            OnPropertyChanged();
        }
    }

    public Point2f GravitateTo
    {
        get => Job.GravitateTo;
        set
        {
            if (Math.Abs(Job.GravitateTo.X - value.X) < 0.001 && Math.Abs(Job.GravitateTo.Y - value.Y) < 0.001)
                return;

            Job.GravitateTo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GravitateText));
        }
    }

    public string? Detect                 { get => Job.Detect;                 set => _f.Forward(value); }
    public string? Mask                   { get => Job.Mask;                   set => _f.Forward(value); }
    public string OutputFolder            { get => Job.OutputFolder;           set => _f.Forward(value); }
    public bool ForceFixed                { get => Job.ForceFixed;             set => _f.Forward(value); }
    public bool Debug                     { get => Job.Debug;                  set => _f.Forward(value); }
    public bool Enhance                   { get => Job.Enhance;                set => _f.Forward(value); }
    public double? OverrideTargetDuration { get => Job.OverrideTargetDuration; set => _f.Forward(value); }
    public float ScoreThreshold           { get => Job.ScoreThreshold;         set { _f.Forward(value); Task.Run(CreatePreview); } }
    public float IdentityThreshold        { get => Job.IdentityThreshold;      set { _f.Forward(value); Task.Run(CreatePreview); } }
    public int? Rotate                    { get => Job.Rotate;                 set { _f.Forward(value); Task.Run(CreatePreview); } }
    public float DetectAbove              { get => Job.DetectAbove;            set { _f.Forward(value); Task.Run(CreatePreview); } }
    public ulong? DetectId                { get => Job.DetectId;               set { _f.Forward(value); Task.Run(CreatePreview); } }

    public JobViewModel(SingleJob job, IThumbnailService thumbnails, Func<string, IObjectTracker> trackerFactory, ILogger log)
    {
        _f               = new ViewModelForwarder<SingleJob>(job, this.OnPropertyChanged);
        _thumbnails      = thumbnails;
        _trackerFactory  = trackerFactory;
        _log             = log;

        ParametersList.Add(new ParameterEntry("DropoutToleranceFrames"      , ""));
        ParametersList.Add(new ParameterEntry("EmaFactor"                   , ""));
        ParametersList.Add(new ParameterEntry("CameraEasing"                , ""));
        ParametersList.Add(new ParameterEntry("LostFreezeFrames"            , ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorSampleCount" , ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorSampleLength", ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorFrameWidth"  , ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorFrameHeight" , ""));

        foreach (var entry in ParametersList)
        {
            entry.PropertyChanged += OnParameterChanged;
        }

        PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(Probe))
            {
                if (Segments.Count == 0)
                    GenerateSegments();
                OnPropertyChanged(nameof(DurationSeconds));
            }
        };
        ParametersList.CollectionChanged += OnParametersCollectionChanged;

        StepForwardCommand  = new RelayCommand(StepForward);
        StepBackwardCommand = new RelayCommand(StepBackward);
        PlayPreviewCommand  = new RelayCommand(PlayPreview);

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _debounceTimer.Tick += DebounceTimerTick;
    }

    public void GenerateSegments()
    {
        Segments.Clear();
        if (Probe == null || Probe.Duration <= 0)
            return;
        var duration = SegmentDuration;
        var segments = (int)Math.Ceiling(Probe.Duration / duration);
        for (int i = 0; i < segments; i++)
        {
            var start = i * duration;
            var end   = Math.Min(start + duration, Probe.Duration);
            Segments.Add(new Segment(start, end));
        }
    }

    public void CopyFrom(JobViewModel src)
    {
        Job.CopyFrom(src.Job);
        OnPropertyChanged(string.Empty); // Refresh all properties
    }

    public async Task CreatePreview()
    {
        if ( Probe == null)
            return;
        try
        {
            var pos = TimeSpan.FromSeconds(PositionSeconds);

            Bitmap? frame;
            if (Preview?.Frame == null || Preview.Position != pos) 
                frame = await _thumbnails.CreateThumbnailAsync(Job.InputFile, Probe, pos, Probe.Width, Probe.Height, Job.Rotate);
            else
                frame = Preview.Frame;
            if (  frame == null )
                return;

            Preview      = new PreviewData(frame, [], null, Job.GravitateTo, pos, Job.Rotate);

            var tracker = _trackerFactory(Job.Detect ?? "");
            var j = new SingleTask
            (
                Job             : Job,
                Info            : Probe,
                OutputFileName  : "preview.jpg",
                SegmentIndex    : 0,
                TotalSegments   : 1,
                SegmentStart    : PositionSeconds,
                SegmentLength   : 1, // 1 second segment for detection
                ProcessorFactory: _ => throw new NotImplementedException()
            );

            var (detections, primaryDetection) = tracker.SelectTrackedObject(j, frame.ToMatContinuous(), j.Job.GravitateTo);

            Rect? crop = null;
            var w = Probe.Width;
            var h = Probe.Height;

            var cropWidth  = Job.Crop?.width  ?? CommandLine.DefaultW;
            var cropHeight = Job.Crop?.height ?? CommandLine.DefaultH;

            var p = primaryDetection?.Center ?? new Point2f(w * Job.GravitateTo.X, h * Job.GravitateTo.Y);

            var cx = p.X - cropWidth  / 2f;
            var cy = p.Y - cropHeight / 2f;

            var r = new Rect(cx, cy, cropWidth, cropHeight);

            crop = ClampCrop(r, w, h);

            Preview = new PreviewData(frame, detections ?? [], crop, Job.GravitateTo, pos, Job.Rotate);
            OnPropertyChanged(nameof(SegmentDuration));
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating preview for {FileName}: {ex.Message}");
        }
    }

    private static Rect ClampCrop(Rect r, float w, float h)
    {
        var x = r.X;
        var y = r.Y;
        var cw = r.Width;
        var ch = r.Height;

        if (x < 0) x = 0;
        if (y < 0) y = 0;

        if (x + cw > w) x = w - cw;
        if (y + ch > h) y = h - ch;

        if (x < 0) x = 0;
        if (y < 0) y = 0;

        return new Rect(x, y, cw, ch);
    }


    private void OnParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ParameterEntry p && e.PropertyName == nameof(ParameterEntry.Value))
        {
            Job.Parameters[p.Key] = p.Value;
        }
    }

    private void OnParametersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ParameterEntry p in e.NewItems)
            {
                Job.Parameters[p.Key] = p.Value;
                p.PropertyChanged += OnParameterChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (ParameterEntry p in e.OldItems)
            {
                Job.Parameters.Remove(p.Key);
                p.PropertyChanged -= OnParameterChanged;
            }
        }
    }

    private void StepForward()
    {
        if (Segments.Count <= 1)
            return;

        var current = GetCurrentSegment();
        if ( current < 0 || current >= Segments.Count - 1 )
            return;

        SliderLiveValue = Segments[current + 1].Start;
    }

    private void StepBackward()
    {
        if (Segments.Count <= 0)
            return;

        var current = GetCurrentSegment();
        if (current <= 0)
        {
            SliderLiveValue = 0;
            return;
        }

        if (SliderLiveValue > Segments[current].Start)
            SliderLiveValue = Segments[current].Start;
        else
            SliderLiveValue = Segments[current - 1].Start;
    }

    private void PlayPreview()
    {
        // Implementation for playing preview
    }

    private int GetCurrentSegment()
    {
        double pos = SliderLiveValue;

        for (int i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            if (pos < s.Start)
                return i - 1;
            if (pos == s.Start)
                return i;
        }

        return -1;
    }

    partial void OnSliderLiveValueChanged(double value)
    {
        // Restart debounce timer on every slider update
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();

        // Commit the final value
        PositionSeconds = SliderLiveValue;
    }

    partial void OnPositionSecondsChanged(double value)
    {
        Task.Run(CreatePreview);
    }

    public async Task<Avalonia.Media.Imaging.Bitmap?> GetThumbnail(double positionSec)
    {
        if (Probe == null)
            return null;

        var pos = TimeSpan.FromSeconds(positionSec);
        var frame = await _thumbnails.CreateThumbnailAsync(Job.InputFile, Probe, pos, ThumbnailService.ThumbWidth, ThumbnailService.ThumbHeight, Job.Rotate).ConfigureAwait(false);
        //frame.Save($"c:\\temp\\thmb-{positionSec:N4}.png");
        return frame;
    }

}

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
    private SingleJob Job { get; }

    public SingleJob GetJob() => Job;

    [ObservableProperty] private VideoInfo?    _probe;
    [ObservableProperty] private PreviewData?  _preview = new(null, [], null, new(0.5f, 0.5f), TimeSpan.Zero, null);
    [ObservableProperty] private Bitmap?       _thumbnail;
    [ObservableProperty] private double        _sliderLiveValue;
    [ObservableProperty] private double        _positionSeconds;

    public string InputFile       => Job.InputFile;
    public double DurationSeconds => Probe?.Duration ?? 0;

    public IRelayCommand StepForwardCommand  { get; }
    public IRelayCommand StepBackwardCommand { get; }

    private readonly IThumbnailService             _thumbnails;
    private readonly DispatcherTimer               _debounceTimer;
    private readonly Func<string, IObjectDetector> _detectorFactory;
    private readonly ILogger                       _log;

    public string FileName => Path.GetFileName(Job.InputFile);

    public string TextDesc => Probe != null 
        ? $"{Probe.Width}x{Probe.Height}, {TimeSpan.FromSeconds(Probe.Duration).ToString(@"hh\:mm\:ss")}), FPS: {Probe.Fps:F2}, Bitrate: {Probe.Bitrate/1024/1024:F2} MB/s"
        : "";

    public override string ToString() => $"{FileName}: {TextDesc}";

    public ObservableCollection<ParameterEntry> ParametersList { get; }
        = new();

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

    public string? Detect
    {
        get => Job.Detect;
        set
        {
            if (Job.Detect == value)
                return;
            Job.Detect = value;
            OnPropertyChanged();
        }
    }

    public float ScoreThreshold
    {
        get => Job.ScoreThreshold;
        set
        {
            if (Math.Abs(Job.ScoreThreshold - value) < 0.001)
                return;
            Job.ScoreThreshold = value;
            OnPropertyChanged();
            Task.Run(CreatePreview);
        }
    }

    public string? Mask
    {
        get => Job.Mask;
        set
        {
            if (Job.Mask == value)
                return;
            Job.Mask = value;
            OnPropertyChanged();
        }
    }

    public string OutputFolder
    {
        get => Job.OutputFolder;
        set
        {
            if (Job.OutputFolder == value)
                return;
            Job.OutputFolder = value;
            OnPropertyChanged();
        }
    }

    public bool ForceFixed
    {
        get => Job.ForceFixed;
        set
        {
            if (Job.ForceFixed == value)
                return;
            Job.ForceFixed = value;
            OnPropertyChanged();
        }
    }

    public bool Debug
    {
        get => Job.Debug;
        set
        {
            if (Job.Debug == value)
                return;
            Job.Debug = value;
            OnPropertyChanged();
        }
    }

    public bool Enhance
    {
        get => Job.Enhance;
        set
        {
            if (Job.Enhance == value)
                return;
            Job.Enhance = value;
            OnPropertyChanged();
        }
    }

    public int? Rotate
    {
        get => Job.Rotate;
        set
        {
            Job.Rotate = value;
            OnPropertyChanged();
            Task.Run(CreatePreview);
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

    public float DetectAbove
    {
        get => Job.DetectAbove;
        set
        {
            if (Math.Abs(Job.DetectAbove - value) < 0.001 )
                return;
            Job.DetectAbove = value;
            OnPropertyChanged();
            Task.Run(CreatePreview);
        }
    }

    public double? OverrideTargetDuration
    {
        get => Job.OverrideTargetDuration;
        set
        {
            if (Job.OverrideTargetDuration != null && value != null && Math.Abs(Job.OverrideTargetDuration.Value - value.Value) < 0.01)
                return;
            Job.OverrideTargetDuration = value;
            OnPropertyChanged();
        }
    }
    public JobViewModel(SingleJob job, IThumbnailService thumbnails, Func<string, IObjectDetector> detectorFactory, ILogger log)
    {
        Job              = job;
        _thumbnails      = thumbnails;
        _detectorFactory = detectorFactory;
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
                OnPropertyChanged(nameof(DurationSeconds));
            }
        };
        ParametersList.CollectionChanged += OnParametersCollectionChanged;

        StepForwardCommand  = new RelayCommand(StepForward);
        StepBackwardCommand = new RelayCommand(StepBackward);

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _debounceTimer.Tick += DebounceTimerTick;
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

            var detector = _detectorFactory(Job.Detect ?? "");
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
            var detections = detector.DetectAll(j, frame.ToMatContinuous());

            Rect? crop = null;
            if (detections.Count > 0)
            {
                var primaryDetection = detections
                    .OrderByDescending(d => d.box.Height * d.box.Width)
                    .FirstOrDefault();

                var w = Probe.Width;
                var h = Probe.Height;

                var cropWidth  = Job.Crop?.width  ?? CommandLine.DefaultW;
                var cropHeight = Job.Crop?.height ?? CommandLine.DefaultH;

                var cx = primaryDetection.center.X - cropWidth  / 2f;
                var cy = primaryDetection.center.Y - cropHeight / 2f;

                var r = new Rect(cx, cy, cropWidth, cropHeight);

                crop = ClampCrop(r, w, h);
            }

            var boxes = detections.Select(x => x.box).ToList();
            Preview = new PreviewData(frame, boxes, crop, Job.GravitateTo, pos, Job.Rotate);
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
        if (DurationSeconds <= 0)
            return;

        var step = DurationSeconds * 0.1; // 10% of total duration

        SliderLiveValue = Math.Min(DurationSeconds, SliderLiveValue + step);
        // trigger seek in your playback pipeline here
    }

    private void StepBackward()
    {
        if (DurationSeconds <= 0)
            return;

        var step = DurationSeconds * 0.1; // 10% of total duration

        SliderLiveValue = Math.Max(0, SliderLiveValue - step);
        // trigger seek in your playback pipeline here
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

}

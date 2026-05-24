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
    public SingleJob Job { get; }
    
    [ObservableProperty] private VideoInfo?    _probe;
    [ObservableProperty] private PreviewData?  _preview = new(null, [], null);
    [ObservableProperty] private ProgressInfo? _progress;
    [ObservableProperty] private Bitmap?       _thumbnail;
    [ObservableProperty] private string        _suggestedAction = "";
    [ObservableProperty] private double        _sliderLiveValue;
    [ObservableProperty] private double        _positionSeconds;

    public double DurationSeconds => Probe?.Duration ?? 0;

    public IRelayCommand StepForwardCommand { get; }
    public IRelayCommand StepBackwardCommand { get; }

    private readonly IThumbnailService             _thumbnails;
    private readonly IFileProbeService             _fileProbe;
    private readonly DispatcherTimer               _debounceTimer;
    private readonly Func<string, IObjectDetector> _detectorFactory;
    private readonly ILogger                       _log;

    public string FileName => Path.GetFileName(Job.InputFile);

    public string TextDesc => Probe != null 
        ? $"{Probe.Width}x{Probe.Height}, {TimeSpan.FromSeconds(Probe.Duration).ToString(@"hh\:mm\:ss")}), FPS: {Probe.Fps:F2}, Bitrate: {Probe.Bitrate/1024/1024:F2} MB/s"
        : "";

    public override string ToString() => $"{FileName} - {TextDesc}";

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
                Job.GravitateTo = null;
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

    public JobViewModel(SingleJob job, IThumbnailService thumbnails, IFileProbeService fileProbe, Func<string, IObjectDetector> detectorFactory, ILogger log)
    {
        Job              = job;
        _thumbnails      = thumbnails;
        _fileProbe       = fileProbe;
        _detectorFactory = detectorFactory;
        _log             = log;

        ParametersList.Add(new ParameterEntry("DropoutToleranceFrames", ""));
        ParametersList.Add(new ParameterEntry("EmaFactor", ""));
        ParametersList.Add(new ParameterEntry("CameraEasing", ""));
        ParametersList.Add(new ParameterEntry("LostFreezeFrames", ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorSampleCount", ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorSampleLength", ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorFrameWidth", ""));
        ParametersList.Add(new ParameterEntry("RotationDetectorFrameHeight", ""));

        foreach (var entry in ParametersList)
        {
            entry.PropertyChanged += OnParameterChanged;
        }

        ParametersList.CollectionChanged += OnParametersCollectionChanged;

        StepForwardCommand = new RelayCommand(StepForward);
        StepBackwardCommand = new RelayCommand(StepBackward);

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _debounceTimer.Tick += DebounceTimerTick;

        _ = Task.Run( LoadThumbnailAsync );
    }

    private async Task LoadThumbnailAsync()
    {
        try
        {
            Probe           = await _fileProbe.ProbeAsync(Job);
            Thumbnail       = await _thumbnails.CreateThumbnailAsync(Job.InputFile, Probe, rotateDegree: Job.Rotate);
            SuggestedAction = Probe.Rotation == 0 ? "crop" : "rotate";
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating thumbnail for {FileName}: {ex.Message}");
        }

        await CreatePreview();
    }

    private async Task CreatePreview()
    {
        if ( Probe == null)
            return;
        try
        {
            var frame    = await _thumbnails.CreateThumbnailAsync(Job.InputFile, Probe, TimeSpan.FromSeconds(PositionSeconds), Probe.Width, Probe.Height, Job.Rotate);
            if (  frame == null )
                return;

            Preview      = new PreviewData(frame, [], null);

            var detector = _detectorFactory(Job.Detect ?? "");
            var detections = detector.DetectAll(frame.ToMatContinuous());

            var boxes = detections.Select(x => new Avalonia.Rect(x.box.X, x.box.Y, x.box.Width, x.box.Height)).ToList();
            Preview = new PreviewData(frame, boxes, null);
        }
        catch (Exception ex)
        {
            _log.LogError($"Error creating preview for {FileName}: {ex.Message}");
        }
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

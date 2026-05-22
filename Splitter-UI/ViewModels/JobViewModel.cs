using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class JobViewModel : ObservableObject
{
    public SingleJob   Job        { get; }
    public VideoInfo?   Probe     { get; set; }
    public PreviewData? Preview   { get; set; }
    public ProgressInfo? Progress { get; set; }
    
    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private string _suggestedAction = "";

    private readonly IThumbnailService _thumbnails;
    private readonly IFileProbeService _fileProbe;

    public string FileName => Path.GetFileName(Job.InputFile);

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
        }
    }

    public JobViewModel(SingleJob job, IThumbnailService thumbnails, IFileProbeService fileProbe)
    {
        Job         = job;
        _thumbnails = thumbnails;
        _fileProbe  = fileProbe;

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


        _ = Task.Run( LoadThumbnailAsync );
    }

    private async Task LoadThumbnailAsync()
    {
        Probe           = await _fileProbe.ProbeAsync(Job);
        Thumbnail       = await _thumbnails.CreateThumbnailAsync(Job.InputFile, Probe);
        SuggestedAction = Probe.Rotation == 0 ? "crop" : "rotate";
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

}

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class FileJobViewModel : ObservableObject
{
    public SingleJob   Job        { get; }
    public VideoInfo?   Probe     { get; set; }
    public PreviewData? Preview   { get; set; }
    public ProgressInfo? Progress { get; set; }
    
    [ObservableProperty]
    private Bitmap? _thumbnail;

    public string FileName { get; set; }


    [ObservableProperty]
    private string _suggestedAction = "";

    private readonly IThumbnailService _thumbnails;
    private readonly IFileProbeService _fileProbe;

    public FileJobViewModel(SingleJob job, IThumbnailService thumbnails, IFileProbeService fileProbe)
    {
        Job         = job;
        _thumbnails = thumbnails;
        _fileProbe  = fileProbe;

        FileName    = Path.GetFileName(job.InputFile);

        _ = Task.Run( LoadThumbnailAsync );
    }

    private async Task LoadThumbnailAsync()
    {
        Probe           = await _fileProbe.ProbeAsync(Job);
        Thumbnail       = await _thumbnails.CreateThumbnailAsync(Job.InputFile, Probe);
        SuggestedAction = Probe.Rotation == 0 ? "crop" : "rotate";
    }
}

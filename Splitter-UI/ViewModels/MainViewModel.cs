using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public FileListViewModel FileList       { get; }
    public PreviewPaneViewModel Preview     { get; }
    public InspectorPaneViewModel Inspector { get; }
    public StatusBarViewModel StatusBar     { get; }
    public LogPaneViewModel LogPane         { get; }
    public ProgressViewModel Progress       { get; }
    private IJobProcessor _processor = null!;

    [ObservableProperty] private bool _transformMode = false;
    private ILogger _logger;

    private CancellationTokenSource? _cancellationTokenSource;

    public MainViewModel(
        FileListViewModel fileListVM,
        PreviewPaneViewModel ppVM,
        InspectorPaneViewModel iVM,
        LogPaneViewModel lpVM,
        StatusBarViewModel sbVM,
        ProgressViewModel pVM,
        IJobProcessor processor,
        ILogger logger
        )
    {
        FileList   = fileListVM;
        Preview    = ppVM;
        Inspector  = iVM;
        LogPane    = lpVM;
        StatusBar  = sbVM;
        Progress   = pVM;
        _processor = processor;
        _logger = logger;

        // Wire selection -> preview + inspector
        FileList.SelectedFileChanged += file =>
        {
            Preview.Selected = file;
            Inspector.Selected = file;
        };

        Progress.SetMain(this);
        Inspector.SetMain(this);
        Inspector.Files = FileList.Files;
    }

    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    public async Task Start()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            StatusBar.StatusText = "Processing…";
            StatusBar.Percent    = 0;
            TransformMode        = true;

            var files = FileList.Files.ToList();
            var jobs = new List<SingleTask>();

            foreach (var file in files)
            {
                var fileJobs = await _processor.GenerateJobs(file.GetJob(), false, _cancellationTokenSource.Token);
                jobs.AddRange(fileJobs);
            }

            await _processor.ProcessJobs(jobs, false, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            // Handle exception
            StatusBar.StatusText = "Error occurred…";
            _logger.LogError($"Error: {ex.Message}");
            _cancellationTokenSource.Cancel();
        }
        finally
        {
            StatusBar.StatusText = "Ready…";
            StatusBar.Percent    = 0;
            TransformMode        = false;

            _cancellationTokenSource?.Dispose();
        }
    }

}

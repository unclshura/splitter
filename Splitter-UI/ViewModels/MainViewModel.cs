using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public FileListViewModel FileList { get; }
    public PreviewPaneViewModel Preview { get; } = new PreviewPaneViewModel();
    public InspectorPaneViewModel Inspector { get; } = new InspectorPaneViewModel();
    public StatusBarViewModel StatusBar { get; } = new StatusBarViewModel();
    public LogPaneViewModel LogPane { get; } = new LogPaneViewModel();

    public MainViewModel(IFileJobFactory fileJobFactory, IAutoDecisionService autoDecisionService)
    {
        FileList = new FileListViewModel(fileJobFactory, autoDecisionService);
        // Wire selection → preview + inspector
        FileList.SelectedFileChanged += file =>
        {
            Preview.Selected = file;
            Inspector.Selected = file;
        };
    }

    [RelayCommand]
    private void Start()
    {
        StatusBar.StatusText = "Processing…";
        // call IProcessingService here
    }

    [RelayCommand]
    private void Stop()
    {
        StatusBar.StatusText = "Stopped";
    }
}

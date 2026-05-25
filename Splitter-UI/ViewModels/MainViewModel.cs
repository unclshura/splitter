using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public FileListViewModel FileList       { get; }
    public PreviewPaneViewModel Preview     { get; }
    public InspectorPaneViewModel Inspector { get; }
    public StatusBarViewModel StatusBar     { get; }
    public LogPaneViewModel LogPane         { get; }

    public MainViewModel(
        IFileJobFactory fileJobFactory,
        IAutoDecisionService autoDecisionService,
        PreviewPaneViewModel ppVM,
        InspectorPaneViewModel iVM,
        LogPaneViewModel lpVM,
        StatusBarViewModel sbVM
        )
    {
        FileList  = new FileListViewModel(fileJobFactory, autoDecisionService);
        Preview   = ppVM;
        Inspector = iVM;
        LogPane   = lpVM;
        StatusBar = sbVM;
        // Wire selection → preview + inspector
        FileList.SelectedFileChanged += file =>
        {
            Preview.Selected = file;
            Inspector.Selected = file;
        };

        Inspector.Files = FileList.Files;
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

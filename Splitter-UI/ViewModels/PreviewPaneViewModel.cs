using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class PreviewPaneViewModel : ObservableObject
{
    [ObservableProperty]
    private FileJobViewModel? _selected;

    public PreviewPaneViewModel()
    {
    }
}

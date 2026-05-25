using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class StatusBarViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _percent;

}

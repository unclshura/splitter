using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class InspectorPaneViewModel : ObservableObject
{
    [ObservableProperty]
    private FileJobViewModel? _selected;

    public List<string> DetectModes => 
        [  
            "face", "body", "none"
        ];

    [RelayCommand]
    private void ApplyOverrides()
    {
        if (Selected is null)
            return;

    }
}

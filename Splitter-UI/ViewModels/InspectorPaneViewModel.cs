using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class InspectorPaneViewModel : ObservableObject
{
    [ObservableProperty]
    private JobViewModel? _selected;

    public ObservableCollection<JobViewModel> Files { get; set; } = [];

    public List<string> DetectModes => 
        [  
            "face", "body", "none"
        ];

    [RelayCommand]
    private void TransformAll()
    {
        _ = _main.Start();
    }


    [RelayCommand]
    private void ApplyOverrides()
    {
        if (Selected is null)
            return;

        foreach (JobViewModel job in Files.Where(x => !ReferenceEquals(x, Selected)))
        {
            job.Detect                 = Selected.Detect;
            job.Rotate                 = Selected.Rotate;
            job.CropText               = Selected.CropText;
            job.ForceFixed             = Selected.ForceFixed;
            job.GravitateText          = Selected.GravitateText;
            job.Mask                   = Selected.Mask;
            job.OutputFolder           = Selected.OutputFolder;
            job.OverrideTargetDuration = Selected.OverrideTargetDuration;
            job.PassthroughText        = Selected.PassthroughText;
            job.Enhance                = Selected.Enhance;
            
            job.ParametersList.Clear();
            foreach (var param in Selected.ParametersList)
                job.ParametersList.Add(param);
        }
    }

    public IRelayCommand RotateLeftCommand { get; }
    public IRelayCommand RotateRightCommand { get; }

    private MainViewModel _main = null!;

    public InspectorPaneViewModel()
    {
        RotateLeftCommand = new RelayCommand(() => AdjustRotation(-90));
        RotateRightCommand = new RelayCommand(() => AdjustRotation(+90));
    }

    public void SetMain(MainViewModel main) => _main = main;

    private void AdjustRotation(int delta)
    {
        if ( Selected == null)
            return;

        var r = Selected.Rotate;
        r = (r + delta) % 360;
        if (r < 0) r += 360;

        Selected.Rotate = r;
    }

}

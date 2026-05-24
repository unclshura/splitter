using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using splitter.algo;

namespace Splitter_UI.ViewModels;

public partial class PreviewPaneViewModel : ObservableObject
{
    [ObservableProperty]
    private JobViewModel? _selected;

    public PreviewData? Preview => Selected?.Preview;
    public Point2f? Sar         => Selected?.Probe?.Sar;
    public int Rotate           => Selected?.Rotate ?? 0;

    partial void OnSelectedChanged(JobViewModel? oldValue, JobViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= SelectedPropertyChanged;

        if (newValue != null)
            newValue.PropertyChanged += SelectedPropertyChanged;

        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Sar));
        OnPropertyChanged(nameof(Rotate));
    }

    private void SelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobViewModel.Preview))
            OnPropertyChanged(nameof(Preview));

        if (e.PropertyName == nameof(JobViewModel.Probe))
        {
            OnPropertyChanged(nameof(Sar));
            OnPropertyChanged(nameof(Rotate));
        }
    }
}


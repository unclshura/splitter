using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class PreviewPaneViewModel : ObservableObject
{
    [ObservableProperty]
    private JobViewModel? _selected;

    public PreviewData? Preview => Selected?.Preview;

    partial void OnSelectedChanged(JobViewModel? oldValue, JobViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= SelectedPropertyChanged;

        if (newValue != null)
            newValue.PropertyChanged += SelectedPropertyChanged;

        OnPropertyChanged(nameof(Preview));
    }

    private void SelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobViewModel.Preview))
            OnPropertyChanged(nameof(Preview));
    }
}


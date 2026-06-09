using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public partial class PreviewPaneViewModel : ObservableObject
{
    [ObservableProperty]
    private JobViewModel? _selected;

    public PreviewData? Preview => Selected?.Preview;
    public Point2f? Sar         => Selected?.Probe?.Sar;
    public int Rotate           => Selected?.Rotate ?? 0;
    public Point2f GravitateTo
    {
        get => Selected?.GravitateTo ?? new Point2f(0.5f, 0.5f);
        set
        {
            if (Selected == null)
                return;
            Selected.GravitateTo = value;
            OnPropertyChanged(nameof(GravitateTo));
        }
    }

    public float DetectAbove
    {
        get => Selected?.DetectAbove ?? 0.7f;
        set
        {
            if (Selected == null)
                return;
            Selected.DetectAbove = value;
            OnPropertyChanged(nameof(DetectAbove));
        }
    }

    public ulong? TrackedId
    {
        get => Selected?.DetectId;
        set
        {
            if (Selected == null)
                return;
            Selected.DetectId = value;
            OnPropertyChanged(nameof(TrackedId));
        }
    }

    partial void OnSelectedChanged(JobViewModel? oldValue, JobViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= SelectedPropertyChanged;

        if (newValue != null)
            newValue.PropertyChanged += SelectedPropertyChanged;

        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Sar));
        OnPropertyChanged(nameof(Rotate));
        OnPropertyChanged(nameof(TrackedId));
        OnPropertyChanged(nameof(DetectAbove));
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


using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.ViewModels;

public record ProgressInfo(string Name, int ProgressLine, double Progress, TimeSpan Eta, double Speed);

public partial class ProgressViewModel : ObservableObject
{
    [ObservableProperty] private int _numberOfProcesses = 0;
    public ObservableCollection<ProgressInfo> Processes { get; } = [];

    private Lock _lock = new();
    public void ClearProgress(string name, int progressLine)
    {
        lock (_lock)
        {
            if (progressLine < 0 || progressLine > Processes.Count)
                return;

            NumberOfProcesses -= 1;
            Processes[progressLine] = new ProgressInfo("", progressLine, 0, TimeSpan.Zero, 0);
        }
    }
    public void DrawProgress(string name, int progressLine, double progress, TimeSpan eta, double speed)
    {
        lock (_lock)
        {
            if (progressLine < 0)
                return;

            while (Processes.Count <= progressLine)
            {
                Processes.Add(new ProgressInfo("", Processes.Count, 0, TimeSpan.Zero, 0));
            }

            if (Processes[progressLine].Name == "")
                NumberOfProcesses += 1;
            Processes[progressLine] = new ProgressInfo(name, progressLine, progress, eta, speed);
        }
    }
}


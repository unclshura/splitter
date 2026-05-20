using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Splitter_UI.ViewModels;

public partial class LogPaneViewModel : ObservableObject
{
    public ObservableCollection<string> Logs { get; } = [];

    public void Add(string message)
    {
        Logs.Add(message);
        if (Logs.Count > 5000)
            Logs.RemoveAt(0);
    }
}

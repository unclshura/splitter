using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Splitter_UI.ViewModels;

public sealed record LogEntry(string Prefix, ConsoleColor Color, string Message);

public partial class LogPaneViewModel : ObservableObject, ILogService
{
    public ObservableCollection<LogEntry> Logs { get; } = [];

    public void Log(string prefix, ConsoleColor color, string msg)
    {
        Add(new LogEntry(prefix.Replace("[", "").Replace("]", ""), color, msg));
    }

    private void Add(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AddInternal(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AddInternal(entry));
        }
    }

    private void AddInternal(LogEntry entry)
    {
        Logs.Add(entry);
        if (Logs.Count > 5000)
            Logs.RemoveAt(0);
    }
}

using Avalonia.Controls;
using Avalonia.Threading;

namespace Splitter_UI.Views;

public partial class LogPane : UserControl
{
    public LogPane()
    {
        InitializeComponent();

        // When DataContext changes, subscribe to collection changes
        this.DataContextChanged += (_, _) =>
        {
            if (DataContext is LogPaneViewModel vm)
            {
                vm.Logs.CollectionChanged += (_, _) => ScrollToEnd();
            }
        };
    }

    private void ScrollToEnd()
    {
        // Must run after layout pass
        Dispatcher.UIThread.Post(() =>
        {
            if (Scroller != null)
                Scroller.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}

using Avalonia.Controls;

namespace Splitter_UI.Views;

public partial class MainWindow : Avalonia.Controls.Window
{
    public MainViewModel Data { get; } = null!; // set by DI
    public MainWindow()
    {
        InitializeComponent();
    }
}
using Avalonia.Controls;

namespace Splitter_UI.Views;

public partial class MainWindow : Avalonia.Controls.Window
{
    public MainViewModel Data { get; } = null!; // set by DI
    public MainWindow()
    {
        InitializeComponent();

        //var uri = new Uri("avares://Splitter-UI/Assets/Fonts/Font Awesome 7 Free-Solid-900.otf");

        //if (!AssetLoader.Exists(uri))
        //{
        //    Console.WriteLine("Resource NOT FOUND: " + uri);
        //    return;
        //}

        //using var stream = AssetLoader.Open(uri);
        //using var ms = new MemoryStream();
        //stream.CopyTo(ms);

        //Console.WriteLine("Resource FOUND. Size = " + ms.Length + " bytes");
    }
}
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Splitter_UI.Views;

public partial class FileListView : UserControl
{
    public static readonly StyledProperty<bool> IsDragActiveProperty =
        AvaloniaProperty.Register<FileListView, bool>(nameof(IsDragActive));

    public bool IsDragActive
    {
        get => GetValue(IsDragActiveProperty);
        set => SetValue(IsDragActiveProperty, value);
    }

    public FileListView()
    {
        InitializeComponent();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            if (DataContext is FileListViewModel vm)
                vm.DeleteSelected();
        }
    }
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        IsDragActive = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        IsDragActive = false;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Avalonia 12:
        // e.Data is IDataObject, but it has NO strongly typed formats.
        if (e.DataTransfer.Contains(DataFormat.File))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;

        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        IsDragActive = false;

        if (DataContext is not FileListViewModel vm)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File))
            return;

        // Avalonia 12:
        // This is the ONLY correct way to get dropped files.
        var items = e.DataTransfer.TryGetFiles();
        if (items is null)
            return;

        var paths = items
            .OfType<IStorageFile>()
            .Select(f => f.Path.LocalPath)
            .ToList();

        if (paths.Count > 0)
            vm.AddFilesCommand.Execute(paths);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class FileListViewModel : ObservableObject
{
    private readonly IFileJobFactory _factory;
    public ObservableCollection<FileJobViewModel> Files { get; } = [];

    [ObservableProperty]
    private FileJobViewModel? _selected;

    public event Action<FileJobViewModel?>? SelectedFileChanged;

    public FileListViewModel(IFileJobFactory factory)
    {
        _factory = factory;
    }

    partial void OnSelectedChanged(FileJobViewModel? value)
        => SelectedFileChanged?.Invoke(value);

    [RelayCommand]
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            // Probe + auto-detect + thumbnail
            var job = new SingleJob { InputFile = path };
            var vm = _factory.Create(job);
            Files.Add(vm);
        }
    }
}

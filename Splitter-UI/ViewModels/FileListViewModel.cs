using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Splitter_UI.ViewModels;

public partial class FileListViewModel : ObservableObject
{
    private readonly IFileJobFactory _factory;
    private readonly IAutoDecisionService _autoDecisionService;
    public ObservableCollection<JobViewModel> Files { get; } = [];
    public ObservableCollection<JobViewModel> SelectedFiles { get; } = [];

    [ObservableProperty]
    private JobViewModel? _selected;

    public event Action<JobViewModel?>? SelectedFileChanged;

    public FileListViewModel(IFileJobFactory factory, IAutoDecisionService autoDecisionService)
    {
        _factory = factory;
        _autoDecisionService = autoDecisionService;
    }

    partial void OnSelectedChanged(JobViewModel? value)
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
            _autoDecisionService.ApplyAutoDecisions(vm, CancellationToken.None);
        }

        Selected = Files.LastOrDefault();
    }

    internal void DeleteSelected()
    {
        if (SelectedFiles.Any()) 
        {
            var toDelete = SelectedFiles.ToList();
            foreach (var item in toDelete)
                Files.Remove(item);
        }
        else if ( Selected != null)
        {
            var sel = Selected;
            Files.Remove(sel);
        }

        Selected = Files.LastOrDefault();
    }
}

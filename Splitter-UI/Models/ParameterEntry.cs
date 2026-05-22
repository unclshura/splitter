using CommunityToolkit.Mvvm.ComponentModel;

namespace Splitter_UI.Models;

public partial class ParameterEntry : ObservableObject
{
    public string Key { get; }
    [ObservableProperty] private string _value;

    public ParameterEntry(string key, string value)
    {
        Key = key;
        Value = value;
    }
}

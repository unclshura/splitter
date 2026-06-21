using System.Reflection;
using System.Runtime.CompilerServices;

namespace Splitter_UI.ViewModels;

internal class ViewModelForwarder<TModel>
{
    public readonly TModel Model;
    private readonly Action<string> _onPropertyChanged;

    public ViewModelForwarder(TModel model, Action<string> onPropertyChanged)
    {
        Model = model;
        _onPropertyChanged = onPropertyChanged;
    }

    public void Forward<T>(
        T newValue,
        [CallerMemberName] string? propertyName = null)
    {
        var modelType = typeof(TModel);
        var prop = modelType.GetProperty(propertyName!, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
            return;

        var oldValue = (T)prop.GetValue(Model)!;

        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            return;

        prop.SetValue(Model, newValue);
        _onPropertyChanged(propertyName!);
    }
}
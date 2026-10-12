using System.ComponentModel;
using System.Reflection;

namespace Ui.Console;

/// <summary>
/// A one-way binding, from the ViewModel to the view: what {Binding Property} does in WPF, without the machinery.
/// It pushes the value a first time, then at each PropertyChanged that concerns it, until Dispose.
/// </summary>
public sealed class Binding : IDisposable
{
    private readonly INotifyPropertyChanged _source;
    private readonly PropertyInfo _property;
    private readonly Action<object?> _toView;

    private Binding(INotifyPropertyChanged source, string property, Action<object?> toView)
    {
        _source = source;
        // As in WPF, the name is a string: a typo only shows at run time (here, a clean exception).
        _property = source.GetType().GetProperty(property)
            ?? throw new ArgumentException($"{source.GetType().Name} has no property \"{property}\".", nameof(property));
        _toView = toView;
        _source.PropertyChanged += Changed;
    }

    public static Binding Bind(INotifyPropertyChanged source, string property, Action<object?> toView, bool pushNow = true)
    {
        var binding = new Binding(source, property, toView);
        if (pushNow)
            binding.Push();
        return binding;
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name means "everything may have changed".
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _property.Name)
            Push();
    }

    private void Push() => _toView(_property.GetValue(_source));

    public void Dispose() => _source.PropertyChanged -= Changed;
}

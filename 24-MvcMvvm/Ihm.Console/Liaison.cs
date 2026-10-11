using System.ComponentModel;
using System.Reflection;

namespace Ihm.Console;

/// <summary>
/// Une liaison à sens unique, du ViewModel vers la vue : ce que fait {Binding Propriete} en WPF, sans la machinerie.
/// Elle pousse la valeur une première fois, puis à chaque PropertyChanged qui la concerne, jusqu'à Dispose.
/// </summary>
public sealed class Liaison : IDisposable
{
    private readonly INotifyPropertyChanged _source;
    private readonly PropertyInfo _propriete;
    private readonly Action<object?> _versLaVue;

    private Liaison(INotifyPropertyChanged source, string propriete, Action<object?> versLaVue)
    {
        _source = source;
        // Comme en WPF, le nom est une chaîne : une faute de frappe ne se voit qu'à l'exécution (ici, une exception nette).
        _propriete = source.GetType().GetProperty(propriete)
            ?? throw new ArgumentException($"{source.GetType().Name} n'a pas de propriété « {propriete} ».", nameof(propriete));
        _versLaVue = versLaVue;
        _source.PropertyChanged += Changement;
    }

    public static Liaison Lier(INotifyPropertyChanged source, string propriete, Action<object?> versLaVue, bool pousserMaintenant = true)
    {
        var liaison = new Liaison(source, propriete, versLaVue);
        if (pousserMaintenant)
            liaison.Pousser();
        return liaison;
    }

    private void Changement(object? emetteur, PropertyChangedEventArgs e)
    {
        // Un nom vide signifie « tout a pu changer ».
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _propriete.Name)
            Pousser();
    }

    private void Pousser() => _versLaVue(_propriete.GetValue(_source));

    public void Dispose() => _source.PropertyChanged -= Changement;
}

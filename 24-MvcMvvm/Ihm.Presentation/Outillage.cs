using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Ihm.Presentation;

// Ce qu'apporteraient CommunityToolkit.Mvvm ou Prism (ObservableObject, RelayCommand, AsyncRelayCommand),
// écrit à la main : c'est court, et on voit qu'il n'y a pas de magie.

/// <summary>La base des ViewModels : prévenir les vues quand une propriété change.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Change le champ et prévient, seulement si la valeur change vraiment (sinon les vues se redessinent pour rien).</summary>
    protected bool Definir<T>(ref T champ, T valeur, [CallerMemberName] string? propriete = null)
    {
        if (EqualityComparer<T>.Default.Equals(champ, valeur))
            return false;
        champ = valeur;
        Signaler(propriete!);
        return true;
    }

    /// <summary>Prévient qu'une propriété calculée (sans champ à elle) a peut-être changé.</summary>
    protected void Signaler(string propriete) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriete));
}

/// <summary>Une commande qu'on peut attendre : la vue console l'attend, les tests aussi.</summary>
public interface ICommandeAsync : ICommand
{
    Task ExecuterAsync();
}

/// <summary>
/// Une commande liée à un bouton. CanExecute décide si le bouton est actif ; quand la réponse peut avoir changé,
/// le ViewModel appelle <see cref="Reevaluer"/> et WPF interroge de nouveau CanExecute.
/// </summary>
public sealed class Commande(Action executer, Func<bool>? peutExecuter = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parametre) => peutExecuter?.Invoke() ?? true;

    public void Execute(object? parametre)
    {
        if (CanExecute(parametre))
            executer();
    }

    public void Reevaluer() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Une commande asynchrone : désactivée PENDANT son exécution, ce qui règle le double clic (un deuxième clic pendant
/// l'appel à la banque ne refait pas le virement).
/// </summary>
public sealed class CommandeAsync(Func<Task> executer, Func<bool>? peutExecuter = null) : ICommandeAsync
{
    private bool _enCours;

    public event EventHandler? CanExecuteChanged;

    public bool EnCours => _enCours;

    public bool CanExecute(object? parametre) => !_enCours && (peutExecuter?.Invoke() ?? true);

    // ICommand.Execute ne renvoie rien : c'est le seul « async void » admis (gestionnaire d'événement).
    // Une exception imprévue remonte au gestionnaire global de l'application ; les erreurs prévues
    // (refus de la banque) sont traitées par le ViewModel lui-même.
    public async void Execute(object? parametre) => await ExecuterAsync();

    public async Task ExecuterAsync()
    {
        if (!CanExecute(null))
            return;
        _enCours = true;
        Reevaluer();
        try
        {
            await executer();
        }
        finally
        {
            _enCours = false;
            Reevaluer();
        }
    }

    public void Reevaluer() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Ce dont un ViewModel a besoin de l'interface sans la connaître : poser une question. WPF répond par une boîte de
/// dialogue, la console par une ligne lue au clavier, les tests par une réponse programmée. Un ViewModel qui
/// appellerait MessageBox.Show ne serait plus testable.
/// </summary>
public interface IConfirmation
{
    Task<bool> DemanderAsync(string question);
}

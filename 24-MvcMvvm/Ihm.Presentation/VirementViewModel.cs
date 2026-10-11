using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Ihm.Banque;

namespace Ihm.Presentation;

/// <summary>
/// Le formulaire de virement, vivant : chaque frappe met à jour le ViewModel (liaison dans les deux sens), qui
/// revérifie tout et prévient la vue. Les erreurs apparaissent pendant la saisie, le bouton s'active quand tout
/// est bon, et le solde de la source est suivi : s'il change, l'erreur « solde insuffisant » aussi.
/// Comparer avec le contrôleur MVC, qui ne voit le formulaire qu'une fois envoyé.
/// </summary>
public sealed class VirementViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly IBanque _banque;
    private readonly IConfirmation _confirmation;
    private readonly Func<Task> _apresVirement;
    private readonly Dictionary<string, string> _erreurs = [];

    private CompteViewModel? _source;
    private CompteViewModel? _cible;
    private string _montantSaisi = "";
    private decimal? _montant;
    private string? _message;
    private bool _messageEstErreur;
    private bool _estOccupe;

    public VirementViewModel(IBanque banque, IConfirmation confirmation, ObservableCollection<CompteViewModel> comptes, Func<Task> apresVirement)
    {
        _banque = banque;
        _confirmation = confirmation;
        _apresVirement = apresVirement;
        Comptes = comptes;
        VirerCommande = new CommandeAsync(VirerAsync, () => PeutVirer);
        EchangerCommande = new Commande(Echanger, () => Source is not null || Cible is not null);
    }

    /// <summary>Les mêmes objets que la liste des comptes : un solde mis à jour l'est partout.</summary>
    public ObservableCollection<CompteViewModel> Comptes { get; }

    public CommandeAsync VirerCommande { get; }

    public Commande EchangerCommande { get; }

    public CompteViewModel? Source
    {
        get => _source;
        set
        {
            var ancienne = _source;
            if (!Definir(ref _source, value))
                return;
            if (ancienne is not null)
                ancienne.PropertyChanged -= SoldeSourceChange;
            if (value is not null)
                value.PropertyChanged += SoldeSourceChange;
            Valider();
        }
    }

    public CompteViewModel? Cible
    {
        get => _cible;
        set
        {
            if (Definir(ref _cible, value))
                Valider();
        }
    }

    /// <summary>Le texte tel que tapé : « abc » doit rester affiché (avec une erreur), pas être remplacé par 0.</summary>
    public string MontantSaisi
    {
        get => _montantSaisi;
        set
        {
            if (Definir(ref _montantSaisi, value ?? ""))
            {
                _montant = Lire(_montantSaisi);
                Valider();
            }
        }
    }

    public string? ErreurMontant => _erreurs.GetValueOrDefault(nameof(MontantSaisi));

    public string? ErreurCible => _erreurs.GetValueOrDefault(nameof(Cible));

    public string? Message
    {
        get => _message;
        private set => Definir(ref _message, value);
    }

    public bool MessageEstErreur
    {
        get => _messageEstErreur;
        private set => Definir(ref _messageEstErreur, value);
    }

    public bool EstOccupe
    {
        get => _estOccupe;
        private set => Definir(ref _estOccupe, value);
    }

    private bool PeutVirer => _source is not null && _cible is not null && _montant is not null && !HasErrors;

    // ─── INotifyDataErrorInfo : WPF entoure en rouge les champs liés aux propriétés en erreur ───

    public bool HasErrors => _erreurs.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propriete) =>
        propriete is not null && _erreurs.TryGetValue(propriete, out var erreur) ? new[] { erreur } : Array.Empty<string>();

    private void Valider()
    {
        string? erreurMontant = null;
        if (_montantSaisi.Trim().Length > 0)
        {
            erreurMontant = _montant is not { } montant
                ? "Saisissez un montant, par exemple 250,50."
                : ReglesVirement.VerifierMontant(montant)
                  ?? (_source is null ? null : ReglesVirement.VerifierSolde(_source.Solde, montant));
        }
        var erreurCible = _source is not null && _cible is not null ? ReglesVirement.VerifierComptes(_source.Id, _cible.Id) : null;

        var avant = HasErrors;
        Noter(nameof(MontantSaisi), erreurMontant, nameof(ErreurMontant));
        Noter(nameof(Cible), erreurCible, nameof(ErreurCible));
        if (avant != HasErrors)
            Signaler(nameof(HasErrors));
        VirerCommande.Reevaluer();
        EchangerCommande.Reevaluer();
    }

    private void Noter(string propriete, string? erreur, string proprieteTexte)
    {
        if (_erreurs.GetValueOrDefault(propriete) == erreur)
            return;
        if (erreur is null)
            _erreurs.Remove(propriete);
        else
            _erreurs[propriete] = erreur;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propriete));
        Signaler(proprieteTexte);
    }

    private void SoldeSourceChange(object? emetteur, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompteViewModel.Solde))
            Valider();
    }

    // ─── Commandes ───

    private async Task VirerAsync()
    {
        var (source, cible, montant) = (_source!, _cible!, _montant!.Value);

        if (montant >= ReglesVirement.SeuilConfirmation
            && !await _confirmation.DemanderAsync($"Virer {Format.Euros(montant)} de {source.Affichage} vers {cible.Affichage} ?"))
        {
            Informer("Virement annulé.", erreur: false);
            return;
        }

        EstOccupe = true;
        Informer("Virement en cours…", erreur: false);
        try
        {
            var virement = await _banque.VirerAsync(source.Id, cible.Id, montant);
            MontantSaisi = "";
            Informer($"Virement de {Format.Euros(virement.Montant)} effectué. Nouveau solde : {Format.Euros(virement.SoldeSource)}.", erreur: false);
            await _apresVirement();
        }
        catch (ErreurMetier erreur)
        {
            // La banque fait foi : entre la saisie et l'envoi, le solde a pu changer ailleurs.
            Informer(erreur.Message, erreur: true);
        }
        finally
        {
            EstOccupe = false;
        }
    }

    private void Echanger() => (Source, Cible) = (Cible, Source);

    private void Informer(string message, bool erreur)
    {
        MessageEstErreur = erreur;
        Message = message;
    }

    /// <summary>« 250 », « 250,5 », « 250.50 », « 1 000 € » : tout ce qu'un utilisateur français peut taper.</summary>
    private static decimal? Lire(string texte)
    {
        var nettoye = new string(texte.Where(c => !char.IsWhiteSpace(c) && c != '€').ToArray()).Replace('.', ',');
        return decimal.TryParse(nettoye, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Format.Francais, out var montant)
            ? montant
            : null;
    }
}

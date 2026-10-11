using System.Collections.ObjectModel;
using Ihm.Banque;

namespace Ihm.Presentation;

/// <summary>Un compte tel que l'écran le montre. Son solde change sur place après un virement : la vue suit.</summary>
public sealed class CompteViewModel(Compte compte) : ObservableObject
{
    private decimal _solde = compte.Solde;

    public Guid Id { get; } = compte.Id;
    public string Titulaire { get; } = compte.Titulaire;
    public string Libelle { get; } = compte.Libelle;
    public string Affichage => $"{Titulaire} — {Libelle}";

    public decimal Solde
    {
        get => _solde;
        internal set
        {
            if (Definir(ref _solde, value))
                Signaler(nameof(SoldeAffiche));
        }
    }

    /// <summary>Mis en forme ici plutôt que dans chaque vue : toutes l'affichent pareil, et un test peut le vérifier.</summary>
    public string SoldeAffiche => Format.Euros(Solde);

    public override string ToString() => Affichage;
}

/// <summary>Une ligne d'historique : rien ne change, donc rien à signaler.</summary>
public sealed class OperationViewModel(Operation operation)
{
    public string Date { get; } = Format.Date(operation.Date);
    public string Libelle { get; } = operation.Libelle;
    public string Montant { get; } = (operation.Montant > 0 ? "+" : "") + Format.Euros(operation.Montant);
    public bool EstDebit { get; } = operation.Montant < 0;
}

/// <summary>
/// La liste des comptes et l'historique du compte sélectionné. Sélectionner un compte (clic dans la liste, liaison
/// SelectedItem) lance le chargement de ses opérations ; rien n'indique d'où vient la sélection, et c'est voulu.
/// </summary>
public sealed class ComptesViewModel(IBanque banque) : ObservableObject
{
    private CompteViewModel? _selection;
    private bool _chargementEnCours;
    private string? _erreur;
    private int _derniereDemande;

    /// <summary>Une ObservableCollection prévient des ajouts et retraits : la liste à l'écran les suit.</summary>
    public ObservableCollection<CompteViewModel> Comptes { get; } = [];

    public ObservableCollection<OperationViewModel> Operations { get; } = [];

    public CompteViewModel? CompteSelectionne
    {
        get => _selection;
        set
        {
            if (Definir(ref _selection, value))
            {
                Signaler(nameof(TitreOperations));
                ChargementOperations = ChargerOperationsAsync(value);
            }
        }
    }

    public string TitreOperations => _selection is null ? "Opérations" : $"Opérations — {_selection.Affichage}";

    /// <summary>Le dernier chargement lancé : la vue l'ignore, les tests l'attendent.</summary>
    public Task ChargementOperations { get; private set; } = Task.CompletedTask;

    public bool ChargementEnCours
    {
        get => _chargementEnCours;
        private set => Definir(ref _chargementEnCours, value);
    }

    public string? Erreur
    {
        get => _erreur;
        private set => Definir(ref _erreur, value);
    }

    /// <summary>Recharge les soldes (en mettant à jour les comptes déjà affichés) et l'historique du compte sélectionné.</summary>
    public async Task ActualiserAsync()
    {
        foreach (var compte in await banque.ComptesAsync())
        {
            if (Comptes.FirstOrDefault(c => c.Id == compte.Id) is { } affiche)
                affiche.Solde = compte.Solde;   // le même objet : la sélection et les listes déroulantes restent en place
            else
                Comptes.Add(new CompteViewModel(compte));
        }

        if (CompteSelectionne is null)
            CompteSelectionne = Comptes.FirstOrDefault();
        else
            ChargementOperations = ChargerOperationsAsync(CompteSelectionne);
        await ChargementOperations;
    }

    private async Task ChargerOperationsAsync(CompteViewModel? compte)
    {
        // Cliquer sur A puis sur B : si la réponse pour A arrive APRÈS celle pour B, il ne faut pas l'afficher
        // sous le titre de B. Chaque chargement a un numéro ; seule la réponse du dernier est gardée.
        var demande = ++_derniereDemande;
        Operations.Clear();
        Erreur = null;
        if (compte is null)
            return;

        ChargementEnCours = true;
        try
        {
            var operations = await banque.OperationsAsync(compte.Id);
            if (demande != _derniereDemande)
                return;
            foreach (var operation in operations)
                Operations.Add(new OperationViewModel(operation));
        }
        catch (ErreurMetier erreur)
        {
            if (demande == _derniereDemande)
                Erreur = erreur.Message;
        }
        finally
        {
            if (demande == _derniereDemande)
                ChargementEnCours = false;
        }
    }
}

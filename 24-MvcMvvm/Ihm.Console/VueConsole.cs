using Ihm.Banque;
using Ihm.Presentation;

namespace Ihm.Console;

/// <summary>
/// La vue console. Comme la fenêtre WPF, elle ne fait que deux choses : écrire dans le ViewModel ce que l'utilisateur
/// saisit, et montrer ce que le ViewModel lui signale. Les lignes « ⚠ », « » » et « [Virer] » apparaissent
/// d'elles-mêmes, poussées par les liaisons au moment où le ViewModel change : la vue ne les a pas demandées.
/// </summary>
public sealed class VueConsole : IDisposable
{
    private readonly EcranPrincipalViewModel _ecran;
    private readonly TextReader _entree;
    private readonly TextWriter _sortie;
    private readonly List<Liaison> _liaisons = [];
    private bool _virerActif;

    public VueConsole(EcranPrincipalViewModel ecran, TextReader entree, TextWriter sortie)
    {
        _ecran = ecran;
        _entree = entree;
        _sortie = sortie;
        var virement = ecran.Virement;

        // Les liaisons de cette vue (en WPF : les {Binding …} du XAML). Pas de valeur initiale : rien à dire au départ.
        _liaisons.Add(Liaison.Lier(virement, nameof(VirementViewModel.ErreurMontant), v => Signal("⚠ montant", v), pousserMaintenant: false));
        _liaisons.Add(Liaison.Lier(virement, nameof(VirementViewModel.ErreurCible), v => Signal("⚠ comptes", v), pousserMaintenant: false));
        _liaisons.Add(Liaison.Lier(virement, nameof(VirementViewModel.Message), v => Signal("»", v), pousserMaintenant: false));

        // Command="{Binding VirerCommande}" : le bouton suit CanExecuteChanged pour se griser ou non.
        virement.VirerCommande.CanExecuteChanged += (_, _) =>
        {
            var actif = virement.VirerCommande.CanExecute(null);
            if (actif != _virerActif)
                _sortie.WriteLine($"  [Virer] {(actif ? "actif" : "grisé")}");
            _virerActif = actif;
        };
    }

    public async Task ExecuterAsync()
    {
        _sortie.WriteLine($"{_ecran.Titre}, vue console. Tapez « aide » pour la liste des commandes.");
        await _ecran.InitialiserAsync();
        AfficherComptes();

        while (await _entree.ReadLineAsync() is { } ligne)
        {
            var mots = ligne.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (mots.Length == 0)
                continue;
            _sortie.WriteLine($"> {ligne.Trim()}");
            var argument = mots.Length > 1 ? mots[1] : "";
            var comptes = _ecran.Comptes.Comptes;
            switch (mots[0].ToLowerInvariant())
            {
                case "comptes":
                    AfficherComptes();
                    break;
                case "voir" when Compte(argument) is { } compte:
                    _ecran.Comptes.CompteSelectionne = compte;
                    await _ecran.Comptes.ChargementOperations;
                    AfficherOperations();
                    break;
                case "de" when Compte(argument) is { } compte:
                    _ecran.Virement.Source = compte;
                    break;
                case "vers" when Compte(argument) is { } compte:
                    _ecran.Virement.Cible = compte;
                    break;
                case "montant":
                    _ecran.Virement.MontantSaisi = argument;
                    break;
                case "echanger":
                    _ecran.Virement.EchangerCommande.Execute(null);
                    _sortie.WriteLine($"  de : {_ecran.Virement.Source?.Affichage ?? "—"} ; vers : {_ecran.Virement.Cible?.Affichage ?? "—"}");
                    break;
                case "virer":
                    // Comme un clic : un bouton grisé ne fait rien.
                    if (!_ecran.Virement.VirerCommande.CanExecute(null))
                        _sortie.WriteLine("  (le bouton Virer est grisé)");
                    else
                        await _ecran.Virement.VirerCommande.ExecuterAsync();
                    break;
                case "quitter" or "q":
                    return;
                case "aide":
                    _sortie.WriteLine("  comptes | voir N | de N | vers N | montant 250,50 | echanger | virer | quitter   (N : numéro du compte)");
                    break;
                default:
                    _sortie.WriteLine("  Commande inconnue (tapez « aide »).");
                    break;
            }

            CompteViewModel? Compte(string texte) =>
                int.TryParse(texte, out var n) && n >= 1 && n <= comptes.Count ? comptes[n - 1] : null;
        }
    }

    private void Signal(string prefixe, object? valeur)
    {
        if (valeur is string texte && texte.Length > 0)
            _sortie.WriteLine($"  {prefixe} {texte}");
    }

    private void AfficherComptes()
    {
        var comptes = _ecran.Comptes.Comptes;
        for (var i = 0; i < comptes.Count; i++)
            _sortie.WriteLine($"  {i + 1}. {comptes[i].Affichage,-24} {comptes[i].SoldeAffiche,14}");
    }

    private void AfficherOperations()
    {
        _sortie.WriteLine($"  {_ecran.Comptes.TitreOperations}");
        foreach (var operation in _ecran.Comptes.Operations)
            _sortie.WriteLine($"    {operation.Date}  {operation.Libelle,-40} {operation.Montant,14}");
    }

    public void Dispose()
    {
        foreach (var liaison in _liaisons)
            liaison.Dispose();
    }
}

/// <summary>La question du ViewModel, posée au clavier.</summary>
public sealed class ConfirmationConsole(TextReader entree, TextWriter sortie) : IConfirmation
{
    public async Task<bool> DemanderAsync(string question)
    {
        sortie.WriteLine($"  ? {question} (o/n)");
        var reponse = await entree.ReadLineAsync();
        sortie.WriteLine($"> {reponse}");
        return reponse?.Trim().ToLowerInvariant() is "o" or "oui";
    }
}

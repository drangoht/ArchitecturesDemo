using System.Globalization;
using Pipeline.Noyau;

namespace Pipeline.Banque;

// Les filtres de l'import d'un fichier de virements, dans l'ordre où on les assemble (voir Imports).
// Chacun fait UNE chose ; aucun ne connaît les autres.

/// <summary>Une ligne CSV « reference;emetteur;beneficiaire;montant;devise » → un virement.</summary>
public sealed class Analyser : IFiltre<string, Virement>
{
    public string Nom => "Analyser";

    public ValueTask<Issue<Virement>> Traiter(string ligne, CancellationToken annulation)
    {
        var champs = ligne.Split(';');
        if (champs.Length != 5)
            return Rejet($"{champs.Length} champs au lieu de 5");
        if (!decimal.TryParse(champs[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var montant))
            return Rejet($"montant « {champs[3]} » illisible");
        var c = champs.Select(x => x.Trim()).ToArray();
        return ValueTask.FromResult(Issue<Virement>.Ok(new Virement(c[0], c[1].ToUpperInvariant(), c[2].ToUpperInvariant(), montant, c[4].ToUpperInvariant())));
    }

    private static ValueTask<Issue<Virement>> Rejet(string detail) => ValueTask.FromResult(Issue<Virement>.Rejet("ligne mal formée", detail));
}

/// <summary>Les règles qui ne demandent que la ligne elle-même.</summary>
public sealed class Valider : IFiltre<Virement, Virement>
{
    public string Nom => "Valider";

    public ValueTask<Issue<Virement>> Traiter(Virement v, CancellationToken annulation) => ValueTask.FromResult(
        string.IsNullOrEmpty(v.Reference) ? Issue<Virement>.Rejet("référence manquante")
        : !Iban.EstValide(v.Emetteur) ? Issue<Virement>.Rejet("IBAN invalide", v.Emetteur)
        : !Iban.EstValide(v.Beneficiaire) ? Issue<Virement>.Rejet("IBAN invalide", v.Beneficiaire)
        : v.Emetteur == v.Beneficiaire ? Issue<Virement>.Rejet("comptes identiques")
        : v.Montant <= 0 ? Issue<Virement>.Rejet("montant invalide", v.Montant.ToString(CultureInfo.InvariantCulture))
        : Issue<Virement>.Ok(v));
}

/// <summary>
/// Écarte une référence déjà vue dans ce fichier, ou déjà exécutée par un import précédent : rejouer un fichier
/// (après une panne, une erreur de manipulation) n'exécute rien deux fois. Filtre À ÉTAT : une seule copie, et un
/// pipeline neuf par import.
/// </summary>
public sealed class Dedupliquer(Registre registre) : IFiltre<Virement, Virement>
{
    private readonly HashSet<string> _vues = new();

    public string Nom => "Dédupliquer";

    public ValueTask<Issue<Virement>> Traiter(Virement v, CancellationToken annulation) => ValueTask.FromResult(
        !_vues.Add(v.Reference) ? Issue<Virement>.Rejet("doublon", "dans le fichier")
        : registre.DejaExecute(v.Reference) ? Issue<Virement>.Rejet("doublon", "déjà exécuté")
        : Issue<Virement>.Ok(v));
}

/// <summary>Montant converti en euros.</summary>
public sealed class Convertir : IFiltre<Virement, Virement>
{
    private static readonly Dictionary<string, decimal> Taux = new() { ["EUR"] = 1m, ["USD"] = 0.92m, ["GBP"] = 1.17m, ["CHF"] = 1.04m };

    public string Nom => "Convertir";

    public ValueTask<Issue<Virement>> Traiter(Virement v, CancellationToken annulation) => ValueTask.FromResult(
        Taux.TryGetValue(v.Devise, out var taux)
            ? Issue<Virement>.Ok(v with { MontantEur = Math.Round(v.Montant * taux, 2) })
            : Issue<Virement>.Rejet("devise inconnue", v.Devise));
}

/// <summary>
/// Le contrôle de fraude : un calcul de score coûteux (simulé : <paramref name="cout"/> × 1 000 tours d'un hachage FNV),
/// un plafond et une liste noire. Sans état : on peut en faire tourner plusieurs copies en parallèle, c'est l'étage le plus lent.
/// </summary>
public sealed class ControlerFraude(int cout, decimal plafond = 10_000m) : IFiltre<Virement, Virement>
{
    private static readonly HashSet<string> ListeNoire = [DonneesDeDemo.Suspect];

    public string Nom => "Contrôler la fraude";

    public ValueTask<Issue<Virement>> Traiter(Virement v, CancellationToken annulation)
    {
        // Du calcul pur, sans allocation : il s'exécute vraiment en parallèle sur plusieurs cœurs.
        var score = 14695981039346656037ul;
        foreach (var c in v.Reference)
            score = (score ^ c) * 1099511628211ul;
        for (var i = 0; i < cout * 1_000; i++)
            score = (score ^ (ulong)i) * 1099511628211ul;

        return ValueTask.FromResult(
            score == 0 ? Issue<Virement>.Rejet("score anormal")
            : ListeNoire.Contains(v.Beneficiaire) ? Issue<Virement>.Rejet("bénéficiaire sur liste noire", v.Beneficiaire)
            : v.MontantEur > plafond ? Issue<Virement>.Rejet("plafond dépassé", $"{v.MontantEur:0.00} €")
            : Issue<Virement>.Ok(v));
    }
}

/// <summary>Le dernier filtre : il débite et crédite. À ÉTAT (les soldes) : une seule copie, dans l'ordre du fichier.</summary>
public sealed class Executer(Registre registre) : IFiltre<Virement, VirementExecute>
{
    public string Nom => "Exécuter";

    public ValueTask<Issue<VirementExecute>> Traiter(Virement v, CancellationToken annulation) => ValueTask.FromResult(
        registre.Virer(v, out var solde) is { } motif
            ? Issue<VirementExecute>.Rejet(motif, v.Reference)
            : Issue<VirementExecute>.Ok(new VirementExecute(v.Reference, v.Emetteur, v.Beneficiaire, v.MontantEur, solde)));
}

/// <summary>Une panne en plein import (un bug, un service tombé) : une exception, pas un rejet. Tout le pipeline s'arrête.</summary>
public sealed class PanneSimulee(string reference) : IFiltre<Virement, Virement>
{
    public string Nom => "Panne simulée";

    public ValueTask<Issue<Virement>> Traiter(Virement v, CancellationToken annulation) =>
        v.Reference == reference
            ? throw new InvalidOperationException($"Panne simulée en traitant {reference}.")
            : ValueTask.FromResult(Issue<Virement>.Ok(v));
}

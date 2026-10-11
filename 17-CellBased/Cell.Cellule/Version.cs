namespace Cell.Cellule;

/// <summary>
/// La version du code qui tourne dans CETTE cellule. Un vrai déploiement remplacerait les binaires ; ici, le plan de contrôle
/// change la version par PUT /admin/version et le comportement suit. Ce qui compte : chaque cellule peut tourner sur
/// une version différente, pendant qu'un déploiement progresse cellule par cellule.
/// </summary>
public sealed class VersionCellule(string initiale)
{
    public static readonly string[] Connues = ["1.0", "1.1", "1.2"];

    private readonly List<string> _historique = [initiale];

    public string Courante
    {
        get { lock (_historique) return _historique[^1]; }
        set { lock (_historique) _historique.Add(value); }
    }

    /// <summary>Toutes les versions qui ont tourné ici, retours arrière compris : on voit jusqu'où un déploiement est allé.</summary>
    public IReadOnlyList<string> Historique
    {
        get { lock (_historique) return [.. _historique]; }
    }

    /// <summary>
    /// Le calcul d'un retrait. La 1.1 contient un bogue (le montant est débité deux fois) : il ne se voit pas à la compilation,
    /// seulement à l'exécution. La 1.2 le corrige.
    /// </summary>
    public decimal Retirer(decimal solde, decimal montant) => Courante == "1.1" ? solde - 2 * montant : solde - montant;

    /// <summary>
    /// La sonde de santé rejoue un retrait sur un compte fictif et vérifie le résultat. C'est elle qui permet au déploiement
    /// d'arrêter une version défectueuse à la première cellule, avant qu'elle n'atteigne tous les clients.
    /// </summary>
    public (bool Sain, string Detail) Sonde()
    {
        var resultat = Retirer(100m, 10m);
        return resultat == 90m
            ? (true, "retrait de contrôle correct")
            : (false, $"retrait de contrôle incorrect : 100 - 10 = {resultat}");
    }
}

/// <summary>Panne simulée de toute la cellule (machine perdue, base corrompue, mauvaise configuration…).</summary>
public sealed class EtatCellule
{
    public bool EnPanne { get; set; }
}

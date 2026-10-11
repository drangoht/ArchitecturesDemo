namespace Espace.Grille;

public enum ModeRoutage
{
    /// <summary>Une clé va toujours à la même unité (hachage) : une seule unité écrit chaque donnée, pas de collision.</summary>
    Affinite,

    /// <summary>Chaque requête va à l'unité suivante : charge bien répartie, mais deux unités peuvent écrire la même donnée.</summary>
    Tourniquet
}

public sealed class AucuneUniteException() : Exception("Aucune unité de traitement disponible.");

/// <summary>
/// La grille de messages : le point d'entrée des requêtes. Elle connaît les unités actives et choisit celle qui traitera
/// chaque requête. S'il n'y en a plus aucune, elle en fait démarrer une (démarrage à froid).
/// </summary>
public sealed class GrilleDeMessages
{
    private readonly List<UniteDeTraitement> _unites = [];
    private readonly Lock _verrou = new();
    private int _tour;

    public ModeRoutage Mode { get; set; } = ModeRoutage.Affinite;

    /// <summary>Fourni par le gestionnaire de déploiement.</summary>
    internal Func<Task>? DemarrageAFroid { get; set; }

    public IReadOnlyList<UniteDeTraitement> Unites()
    {
        lock (_verrou) return _unites.ToList();
    }

    internal void Ajouter(UniteDeTraitement unite)
    {
        lock (_verrou) _unites.Add(unite);
    }

    internal void Retirer(UniteDeTraitement unite)
    {
        lock (_verrou) _unites.Remove(unite);
    }

    public async Task<(object Reponse, string Unite)> Envoyer(IRequete requete)
    {
        for (var essai = 0; ; essai++)
        {
            var unite = Choisir(requete.Cle);
            if (unite is null && DemarrageAFroid is not null)
            {
                await DemarrageAFroid();
                unite = Choisir(requete.Cle);
            }
            if (unite is null)
                throw new AucuneUniteException();

            try
            {
                return (await unite.Traiter(requete), unite.Id);
            }
            catch (UniteEnPanneException) when (essai < 3)
            {
                // L'unité est tombée avant de traiter la requête : on la confie à une autre.
            }
        }
    }

    private UniteDeTraitement? Choisir(string cle)
    {
        lock (_verrou)
        {
            if (_unites.Count == 0)
                return null;
            return Mode == ModeRoutage.Tourniquet
                ? _unites[(int)((uint)Interlocked.Increment(ref _tour) % _unites.Count)]
                // Hachage « de rendez-vous » : la clé va à l'unité qui obtient le meilleur score avec elle. Ajouter ou retirer
                // une unité ne déplace que les clés de cette unité (un modulo les redistribuerait presque toutes).
                : _unites.MaxBy(u => Hachage($"{cle}/{u.Id}"))!;
        }
    }

    /// <summary>FNV-1a : stable d'une exécution à l'autre, contrairement à string.GetHashCode.</summary>
    internal static uint Hachage(string texte)
    {
        var h = 2166136261u;
        foreach (var c in texte)
            h = (h ^ c) * 16777619u;
        return h;
    }
}

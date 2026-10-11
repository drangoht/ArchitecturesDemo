namespace Espace.Grille;

/// <summary>
/// La version d'une entrée : un compteur, et l'unité qui l'a écrite (pour départager deux écritures simultanées).
/// Toutes les répliques et la base appliquent la même règle, « la plus grande version gagne » : elles finissent identiques.
/// </summary>
public readonly record struct Version(long Numero, string Unite) : IComparable<Version>
{
    public static readonly Version Zero = new(0, "");

    public int CompareTo(Version autre) =>
        Numero != autre.Numero ? Numero.CompareTo(autre.Numero) : string.CompareOrdinal(Unite, autre.Unite);

    public static bool operator >(Version a, Version b) => a.CompareTo(b) > 0;
    public static bool operator <(Version a, Version b) => a.CompareTo(b) < 0;

    public override string ToString() => $"v{Numero}@{Unite}";
}

/// <summary>Une donnée de la grille. La valeur est un objet immuable (un record) : on la remplace, on ne la modifie pas.</summary>
public sealed record Entree(string Cle, object Valeur, Version Version);

/// <summary>Une écriture faite par une unité, envoyée aux autres répliques et à la pompe de données.</summary>
public sealed record Modification(string Cle, object Valeur, Version Version, Version Precedente);

/// <summary>Ce qu'a donné l'arrivée d'une modification dans une réplique.</summary>
public readonly record struct ResultatFusion(bool Appliquee, Version? Perdue);

/// <summary>
/// La copie EN MÉMOIRE de toutes les données, propre à une unité de traitement. L'unité y lit et y écrit sans jamais
/// attendre la base ; chaque écriture est ensuite diffusée aux autres répliques, de façon asynchrone.
/// </summary>
public sealed class Replique
{
    private readonly Dictionary<string, Entree> _entrees = new();
    private readonly Lock _verrou = new();
    private readonly Action<Modification> _publier;

    internal Replique(string unite, Action<Modification> publier)
    {
        Unite = unite;
        _publier = publier;
    }

    public string Unite { get; }

    public int Nombre
    {
        get { lock (_verrou) return _entrees.Count; }
    }

    public Entree? Lire(string cle)
    {
        lock (_verrou) return _entrees.GetValueOrDefault(cle);
    }

    public IReadOnlyList<Entree> Toutes()
    {
        lock (_verrou) return _entrees.Values.ToList();
    }

    /// <summary>
    /// Lire, décider, écrire : atomique DANS CETTE RÉPLIQUE. Rien n'empêche une autre unité de faire de même au même moment
    /// sur sa propre réplique : c'est la source des collisions.
    /// </summary>
    internal (object Reponse, Modification? Modification) Modifier<T>(string cle, Func<T?, (T? Nouveau, object Reponse)> decider)
        where T : class
    {
        lock (_verrou)
        {
            var actuelle = _entrees.GetValueOrDefault(cle);
            var (nouveau, reponse) = decider(actuelle?.Valeur as T);
            if (nouveau is null)
                return (reponse, null);

            var precedente = actuelle?.Version ?? Version.Zero;
            var modification = new Modification(cle, nouveau, new Version(precedente.Numero + 1, Unite), precedente);
            _entrees[cle] = new Entree(cle, nouveau, modification.Version);
            // Publié sous le verrou : les modifications d'une unité partent dans l'ordre où elles ont été faites.
            _publier(modification);
            return (reponse, modification);
        }
    }

    /// <summary>
    /// Une modification venue d'une autre unité. Cas normal : elle part de la version qu'on a (<c>Precedente</c>).
    /// Sinon, deux unités ont modifié la même entrée en même temps sans se voir : une COLLISION. La plus grande version
    /// gagne partout, l'autre écriture est perdue, alors que son client a reçu « OK ».
    /// </summary>
    internal ResultatFusion Fusionner(Modification m)
    {
        lock (_verrou)
        {
            var locale = _entrees.GetValueOrDefault(m.Cle)?.Version ?? Version.Zero;
            if (m.Version == locale)
                return new ResultatFusion(false, null);  // déjà connue (copie initiale d'une nouvelle réplique)

            var concurrente = locale != m.Precedente && locale.Unite != "" && locale.Unite != m.Version.Unite;
            if (m.Version > locale)
            {
                _entrees[m.Cle] = new Entree(m.Cle, m.Valeur, m.Version);
                return new ResultatFusion(true, concurrente ? locale : null);
            }
            return new ResultatFusion(false, concurrente ? m.Version : null);
        }
    }

    internal void Charger(IEnumerable<Entree> entrees)
    {
        lock (_verrou)
            foreach (var entree in entrees)
                _entrees[entree.Cle] = entree;
    }
}

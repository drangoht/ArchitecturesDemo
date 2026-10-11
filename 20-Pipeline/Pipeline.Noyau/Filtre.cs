namespace Pipeline.Noyau;

/// <summary>
/// Un FILTRE : il reçoit un élément, en produit un autre (ou le rejette), et ne sait rien d'autre. Ni d'où vient
/// l'élément, ni où il va, ni quels filtres l'entourent : c'est ce qui permet de les réordonner, de les remplacer,
/// de les réutiliser dans un autre pipeline et de les tester un par un.
/// </summary>
public interface IFiltre<in TEntree, TSortie>
{
    string Nom { get; }

    /// <summary>Un rejet est un résultat normal (la ligne est écartée, le flux continue) ; une exception arrête tout le pipeline.</summary>
    ValueTask<Issue<TSortie>> Traiter(TEntree entree, CancellationToken annulation);
}

/// <summary>Ce qu'un filtre fait d'un élément : il le transforme, ou il le rejette avec un motif.</summary>
public readonly record struct Issue<T>
{
    private Issue(T? valeur, string? motif, string? detail)
    {
        Valeur = valeur;
        Motif = motif;
        Detail = detail;
    }

    public T? Valeur { get; }

    /// <summary>Le motif du rejet, court et stable (il sert à compter les rejets par motif).</summary>
    public string? Motif { get; }

    public string? Detail { get; }

    public bool EstRejet => Motif is not null;

    public static Issue<T> Ok(T valeur) => new(valeur, null, null);

    public static Issue<T> Rejet(string motif, string? detail = null) => new(default, motif, detail);
}

/// <summary>Un filtre écrit en une ligne, à partir d'une fonction (pour les tests, ou de petits filtres de composition).</summary>
public sealed class FiltreFonction<TEntree, TSortie>(string nom, Func<TEntree, Issue<TSortie>> fonction) : IFiltre<TEntree, TSortie>
{
    public string Nom => nom;

    public ValueTask<Issue<TSortie>> Traiter(TEntree entree, CancellationToken annulation) => ValueTask.FromResult(fonction(entree));
}

/// <summary>Un élément qui circule dans les tuyaux : sa valeur, et le numéro de ligne d'origine (pour les rejets).</summary>
public sealed record Element<T>(long Ligne, T Valeur);

/// <summary>Un élément écarté par un filtre.</summary>
public sealed record Rejet(long Ligne, string Filtre, string Motif, string? Detail);

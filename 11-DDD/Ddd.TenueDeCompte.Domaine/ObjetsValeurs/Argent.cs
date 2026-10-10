using System.Globalization;

namespace Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

/// <summary>
/// Objet-valeur : défini par ses attributs (pas d'identité), immuable, toujours valide.
/// Deux objets Argent de même montant et même devise sont égaux (c'est ce que donne un record).
/// </summary>
public sealed record Devise
{
    public static readonly Devise Euro = new("EUR");
    public static readonly Devise DollarUs = new("USD");
    public static readonly Devise FrancSuisse = new("CHF");

    private static readonly Dictionary<string, Devise> Connues =
        new[] { Euro, DollarUs, FrancSuisse }.ToDictionary(d => d.Code);

    public string Code { get; }

    private Devise(string code) => Code = code;

    public static Devise De(string code) =>
        Connues.TryGetValue(code.Trim().ToUpperInvariant(), out var devise)
            ? devise
            : throw new RegleMetierException($"Devise inconnue : « {code} ». Devises tenues : {string.Join(", ", Connues.Keys)}.");

    public override string ToString() => Code;
}

public sealed record Argent
{
    public decimal Montant { get; }
    public Devise Devise { get; }

    public Argent(decimal montant, Devise devise)
    {
        if (montant < 0)
            throw new RegleMetierException("Un montant d'argent ne peut pas être négatif.");
        if (decimal.Round(montant, 2) != montant)
            throw new RegleMetierException("Un montant d'argent a au plus deux décimales.");
        Montant = montant;
        Devise = devise;
    }

    public static Argent Zero(Devise devise) => new(0m, devise);

    public bool EstNul => Montant == 0;

    // Les opérations refusent de mélanger les devises : c'est une règle du domaine, portée par le type lui-même.
    public static Argent operator +(Argent a, Argent b) => new(a.Montant + b.Montant, MemeDevise(a, b));
    public static Argent operator -(Argent a, Argent b) => new(a.Montant - b.Montant, MemeDevise(a, b));
    public static bool operator >(Argent a, Argent b) => MemeDevise(a, b) is not null && a.Montant > b.Montant;
    public static bool operator <(Argent a, Argent b) => MemeDevise(a, b) is not null && a.Montant < b.Montant;
    public static bool operator >=(Argent a, Argent b) => !(a < b);
    public static bool operator <=(Argent a, Argent b) => !(a > b);

    private static Devise MemeDevise(Argent a, Argent b) =>
        a.Devise == b.Devise ? a.Devise
            : throw new RegleMetierException($"Impossible de combiner {a.Devise} et {b.Devise} sans conversion.");

    public override string ToString() => $"{Montant.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"))} {Devise}";

    // Format technique (persistance) : « 1234.56 EUR ».
    public string VersTexte() => $"{Montant.ToString(CultureInfo.InvariantCulture)} {Devise.Code}";

    public static Argent DepuisTexte(string texte)
    {
        var morceaux = texte.Split(' ');
        return new Argent(decimal.Parse(morceaux[0], CultureInfo.InvariantCulture), Devise.De(morceaux[1]));
    }
}

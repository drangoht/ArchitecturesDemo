namespace Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

/// <summary>Identifiant fortement typé : impossible de passer un identifiant de virement là où on attend un compte.</summary>
public readonly record struct CompteId(Guid Valeur)
{
    public static CompteId Nouveau() => new(Guid.NewGuid());
    public override string ToString() => Valeur.ToString();
}

public sealed record NomTitulaire
{
    public string Valeur { get; }

    private NomTitulaire(string valeur) => Valeur = valeur;

    public static NomTitulaire Depuis(string saisie)
    {
        var nettoye = string.Join(' ', (saisie ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (nettoye.Length is < 2 or > 70)
            throw new RegleMetierException("Le nom du titulaire doit faire entre 2 et 70 caractères.");
        return new NomTitulaire(nettoye);
    }

    public override string ToString() => Valeur;
}

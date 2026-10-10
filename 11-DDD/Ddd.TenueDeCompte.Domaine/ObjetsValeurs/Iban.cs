namespace Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

/// <summary>
/// Un IBAN ne peut pas exister sous une forme invalide : la validation (clé modulo 97) est faite à la création.
/// Ailleurs dans le code, recevoir un Iban suffit à savoir qu'il est valide : plus besoin de revérifier une chaîne.
/// </summary>
public sealed record Iban
{
    /// <summary>Forme normalisée : sans espaces, en majuscules.</summary>
    public string Valeur { get; }

    private Iban(string valeur) => Valeur = valeur;

    public static Iban Depuis(string saisie)
    {
        var valeur = new string((saisie ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        if (valeur.Length is < 15 or > 34
            || !valeur.All(char.IsAsciiLetterOrDigit)
            || !char.IsAsciiLetter(valeur[0]) || !char.IsAsciiLetter(valeur[1])
            || !char.IsAsciiDigit(valeur[2]) || !char.IsAsciiDigit(valeur[3]))
            throw new RegleMetierException($"IBAN invalide : « {saisie} ».");

        if (Modulo97(valeur[4..] + valeur[..4]) != 1)
            throw new RegleMetierException($"IBAN invalide (clé de contrôle incorrecte) : « {saisie} ».");

        return new Iban(valeur);
    }

    /// <summary>Calcule la clé de contrôle d'un nouvel IBAN à partir du code pays et du BBAN.</summary>
    public static Iban Generer(string codePays, string bban)
    {
        var cle = 98 - Modulo97(bban + codePays + "00");
        return Depuis($"{codePays}{cle:00}{bban}");
    }

    private static int Modulo97(string texte)
    {
        var reste = 0;
        foreach (var caractere in texte)
        {
            var valeur = char.IsAsciiDigit(caractere) ? caractere - '0' : caractere - 'A' + 10;
            reste = valeur < 10 ? (reste * 10 + valeur) % 97 : (reste * 100 + valeur) % 97;
        }
        return reste;
    }

    /// <summary>Forme lisible, par blocs de 4 : « FR76 3000 1000 … ».</summary>
    public override string ToString() => string.Join(' ', Valeur.Chunk(4).Select(bloc => new string(bloc)));
}

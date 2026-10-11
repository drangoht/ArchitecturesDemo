using System.Numerics;

namespace Pipeline.Banque;

/// <summary>Un ordre de virement, tel qu'il circule entre les filtres.</summary>
public sealed record Virement(string Reference, string Emetteur, string Beneficiaire, decimal Montant, string Devise, decimal MontantEur = 0);

/// <summary>Ce qui sort du pipeline d'import : un virement exécuté.</summary>
public sealed record VirementExecute(string Reference, string Emetteur, string Beneficiaire, decimal MontantEur, decimal SoldeEmetteur);

public sealed record CompteDto(string Iban, string Titulaire, decimal Solde, int Operations);

/// <summary>Les comptes de la banque, et les références des virements déjà exécutés (pour ne jamais exécuter deux fois).</summary>
public sealed class Registre
{
    private readonly Dictionary<string, (string Titulaire, decimal Solde, int Operations)> _comptes = new();
    private readonly HashSet<string> _executes = new();
    private readonly Lock _verrou = new();

    public void Ouvrir(string iban, string titulaire, decimal solde)
    {
        lock (_verrou) _comptes[iban] = (titulaire, solde, 0);
    }

    public CompteDto? Consulter(string iban)
    {
        lock (_verrou)
            return _comptes.TryGetValue(iban, out var c) ? new CompteDto(iban, c.Titulaire, c.Solde, c.Operations) : null;
    }

    /// <summary>Tous les soldes : un virement déplace de l'argent, il n'en crée ni n'en détruit.</summary>
    public IReadOnlyDictionary<string, decimal> Soldes()
    {
        lock (_verrou) return _comptes.ToDictionary(c => c.Key, c => c.Value.Solde);
    }

    public bool DejaExecute(string reference)
    {
        lock (_verrou) return _executes.Contains(reference);
    }

    public int NombreExecutes
    {
        get { lock (_verrou) return _executes.Count; }
    }

    /// <summary>Débit et crédit ensemble, ou rien. Renvoie le motif du refus, ou null.</summary>
    public string? Virer(Virement v, out decimal soldeEmetteur)
    {
        lock (_verrou)
        {
            soldeEmetteur = 0;
            if (v.MontantEur <= 0)
                return "montant non converti";  // précondition : le filtre Convertir est passé avant
            if (!_executes.Add(v.Reference))
                return "doublon";
            if (!_comptes.TryGetValue(v.Emetteur, out var emetteur) || !_comptes.TryGetValue(v.Beneficiaire, out var beneficiaire))
            {
                _executes.Remove(v.Reference);
                return "compte inconnu";
            }
            if (v.MontantEur > emetteur.Solde)
            {
                _executes.Remove(v.Reference);
                return "solde insuffisant";
            }
            _comptes[v.Emetteur] = emetteur with { Solde = emetteur.Solde - v.MontantEur, Operations = emetteur.Operations + 1 };
            _comptes[v.Beneficiaire] = beneficiaire with { Solde = beneficiaire.Solde + v.MontantEur, Operations = beneficiaire.Operations + 1 };
            soldeEmetteur = emetteur.Solde - v.MontantEur;
            return null;
        }
    }
}

/// <summary>Contrôle et fabrication d'IBAN (clé ISO 13616 : modulo 97).</summary>
public static class Iban
{
    public static bool EstValide(string iban) =>
        iban.Length is >= 15 and <= 34 && iban.All(char.IsAsciiLetterOrDigit) && Reste(iban[4..] + iban[..4]) == 1;

    /// <summary>Calcule la clé d'un IBAN à partir du BBAN (pour les données de démo).</summary>
    public static string Fabriquer(string bban, string pays = "FR") => $"{pays}{98 - Reste(bban + pays + "00"):D2}{bban}";

    private static int Reste(string texte) =>
        (int)(BigInteger.Parse(string.Concat(texte.ToUpperInvariant().Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()))) % 97);
}

public static class DonneesDeDemo
{
    public const string Alice = "FR7630006000011234567890189";
    public const string Bob = "FR1230006000019876543210452";

    /// <summary>Un compte existant, mais sur la liste noire du contrôle de fraude.</summary>
    public const string Suspect = "FR4330004000030000000000991";

    public static Registre Registre()
    {
        var registre = new Registre();
        registre.Ouvrir(Alice, "Alice", 1_000m);
        registre.Ouvrir(Bob, "Bob", 500m);
        registre.Ouvrir(Suspect, "Société écran", 0m);
        return registre;
    }
}

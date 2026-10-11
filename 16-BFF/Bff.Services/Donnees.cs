namespace Bff.Services;

/// <summary>Repère de l'assemblage, pour les tests (chaque projet de la démo a son propre Program).</summary>
public sealed class Marqueur;

public sealed record Utilisateur(
    string Id, string MotDePasse, string Prenom, string Nom, string Email, string Segment, string ConseillerId, Guid[] Comptes);

public sealed record Compte(
    Guid Id, string Titulaire, string Iban, string Libelle, string Type, string Devise,
    decimal Solde, decimal PlafondRetrait, DateOnly DateOuverture, string Agence);

public sealed record Operation(Guid Id, Guid CompteId, DateTimeOffset Date, string LibelleBrut, string Categorie, decimal Montant);

public sealed record Conseiller(string Id, string Nom, string Telephone, string Email, string Agence);

public sealed record Offre(string Id, string Segment, string Titre, string Texte, string Image, DateOnly FinValidite);

/// <summary>
/// Les données des services « métier ». Ces API sont génériques : elles ne savent rien des écrans qui les appellent,
/// renvoient des objets complets et se consultent un par un. C'est précisément pour cela qu'un BFF est utile.
/// </summary>
public sealed class Banque
{
    public static readonly Guid AliceCourant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid BobCourant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AliceLivret = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Lock _verrou = new();
    private readonly Dictionary<Guid, Compte> _comptes = [];
    private readonly List<Operation> _operations = [];

    public IReadOnlyDictionary<string, Utilisateur> Utilisateurs { get; } = new Dictionary<string, Utilisateur>
    {
        // Mots de passe de démonstration : ce faux fournisseur d'identité n'a pas d'autre usage.
        ["alice"] = new("alice", "demo", "Alice", "Martin", "alice@exemple.fr", "particulier", "c1", [AliceCourant, AliceLivret]),
        ["bob"] = new("bob", "demo", "Bob", "Durand", "bob@exemple.fr", "jeune", "c2", [BobCourant])
    };

    public IReadOnlyDictionary<string, Conseiller> Conseillers { get; } = new Dictionary<string, Conseiller>
    {
        ["c1"] = new("c1", "Claire Dubois", "01 23 45 67 89", "claire.dubois@exemple.fr", "Paris Bastille"),
        ["c2"] = new("c2", "Karim Benali", "04 72 00 00 00", "karim.benali@exemple.fr", "Lyon Part-Dieu")
    };

    public IReadOnlyList<Offre> Offres { get; } =
    [
        new("o1", "particulier", "Prêt immobilier à taux fixe", "Financez votre résidence principale… (texte complet de l'offre, conditions, exemple représentatif)", "/images/immo.jpg", new DateOnly(2026, 12, 31)),
        new("o2", "particulier", "Assurance habitation", "Protégez votre logement… (texte complet, garanties, franchises)", "/images/habitation.jpg", new DateOnly(2026, 11, 30)),
        new("o3", "jeune", "Carte jeune sans frais", "Une carte gratuite jusqu'à 25 ans… (texte complet)", "/images/carte.jpg", new DateOnly(2027, 6, 30))
    ];

    public Banque()
    {
        Ajouter(new Compte(AliceCourant, "alice", "FR76 3000 4000 0100 0000 1111 182", "Compte courant", "Courant", "EUR", 1_000m, 500m, new DateOnly(2018, 3, 12), "Paris Bastille"), 25);
        Ajouter(new Compte(AliceLivret, "alice", "FR76 3000 4000 0100 0000 4444 512", "Livret A", "Epargne", "EUR", 8_000m, 0m, new DateOnly(2019, 9, 1), "Paris Bastille"), 4);
        Ajouter(new Compte(BobCourant, "bob", "FR76 3000 4000 0200 0000 2222 097", "Compte courant", "Courant", "EUR", 500m, 300m, new DateOnly(2024, 1, 15), "Lyon Part-Dieu"), 15);
    }

    public Compte? Compte(Guid id)
    {
        lock (_verrou)
            return _comptes.GetValueOrDefault(id);
    }

    /// <summary>Opérations du compte, les plus récentes d'abord, page par page.</summary>
    public (IReadOnlyList<Operation> Page, int Total) Operations(Guid compteId, int page, int taille)
    {
        lock (_verrou)
        {
            var toutes = _operations.Where(o => o.CompteId == compteId).OrderByDescending(o => o.Date).ToList();
            return (toutes.Skip((page - 1) * taille).Take(taille).ToList(), toutes.Count);
        }
    }

    public Compte Virer(Guid source, Guid cible, decimal montant, DateTimeOffset date)
    {
        lock (_verrou)
        {
            if (montant <= 0 || source == cible)
                throw new InvalidOperationException("Montant positif et comptes différents requis.");
            if (!_comptes.TryGetValue(source, out var s) || !_comptes.TryGetValue(cible, out var c))
                throw new KeyNotFoundException("Compte introuvable.");
            if (s.Solde < montant)
                throw new InvalidOperationException($"Solde insuffisant ({s.Solde:0.00} €).");

            _comptes[source] = s with { Solde = s.Solde - montant };
            _comptes[cible] = c with { Solde = c.Solde + montant };
            _operations.Add(new Operation(Guid.NewGuid(), source, date, $"VIR SEPA EMIS {c.Libelle.ToUpperInvariant()}", "Virement", -montant));
            _operations.Add(new Operation(Guid.NewGuid(), cible, date, $"VIR SEPA RECU {s.Libelle.ToUpperInvariant()}", "Virement", montant));
            return _comptes[source];
        }
    }

    private static readonly (string Libelle, string Categorie, decimal Min, decimal Max)[] Modeles =
    [
        ("CB CARREFOUR {0} PARIS 15", "Alimentation", 12m, 95m),
        ("CB BOULANGERIE DU PONT {0}", "Alimentation", 2m, 12m),
        ("PRLV SEPA EDF", "Logement", 45m, 90m),
        ("PRLV SEPA FREE MOBILE", "Télécom", 10m, 20m),
        ("CB SNCF INTERNET {0}", "Transport", 25m, 120m),
        ("CB FNAC.COM {0}", "Loisirs", 15m, 80m),
        ("CB AMAZON EU SARL {0}", "Achats", 8m, 60m),
        ("RETRAIT DAB {0} LYON", "Retrait", 20m, 60m)
    ];

    /// <summary>Un historique déterministe (graine fixe) : les tests et les exemples de la doc restent stables.</summary>
    private void Ajouter(Compte compte, int nbOperations)
    {
        _comptes[compte.Id] = compte;
        var hasard = new Random(compte.Id.GetHashCode());
        var date = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < nbOperations; i++)
        {
            date = date.AddHours(-hasard.Next(20, 60));
            if (compte.Type == "Epargne")
            {
                _operations.Add(new Operation(Guid.NewGuid(), compte.Id, date, "VIR SEPA DEPUIS COMPTE COURANT", "Épargne", 100m));
                continue;
            }
            if (i % 10 == 4)
            {
                _operations.Add(new Operation(Guid.NewGuid(), compte.Id, date, "VIR SEPA SALAIRE ACME SAS", "Revenus", 2_100m));
                continue;
            }
            var (libelle, categorie, min, max) = Modeles[hasard.Next(Modeles.Length)];
            var montant = Math.Round(min + (decimal)hasard.NextDouble() * (max - min), 2);
            _operations.Add(new Operation(Guid.NewGuid(), compte.Id, date, string.Format(libelle, date.ToString("ddMM")), categorie, -montant));
        }
    }
}

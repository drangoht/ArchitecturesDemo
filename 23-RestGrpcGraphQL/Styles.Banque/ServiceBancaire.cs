using System.Threading.Channels;

namespace Styles.Banque;

public enum TypeCompte { Courant, Epargne }

public sealed record Client(string Id, string Nom, string Email);

public sealed record Operation(string Id, string Numero, DateTimeOffset Date, string Libelle, decimal Montant);

/// <summary>Un compte, tel que le stocke la banque. Toutes ses propriétés ne servent pas à tous les écrans.</summary>
public sealed record Compte(
    string Numero, string ClientId, string Libelle, TypeCompte Type, string Iban, DateOnly DateOuverture,
    decimal PlafondDecouvert, decimal Solde, long Version);

public sealed record Virement(string Id, string Source, string Cible, decimal Montant, string Libelle, DateTimeOffset Date, decimal SoldeSource);

public enum CodeErreur { ClientInconnu, CompteInconnu, VirementInconnu, ParametreInvalide, MontantInvalide, MemeCompte, SoldeInsuffisant }

/// <summary>Une erreur métier. Chaque API la traduit dans sa langue : statut HTTP, statut gRPC, ou entrée de « errors ».</summary>
public sealed class ErreurMetier(CodeErreur code, string message) : Exception(message)
{
    public CodeErreur Code => code;
}

/// <summary>Ce que coûtent les appels à la « base » : de quoi voir les N+1 que cache une seule requête HTTP.</summary>
public sealed class Statistiques
{
    private long _lecturesOperations;

    /// <summary>Le nombre de requêtes « en base » sur les opérations (une lecture groupée de plusieurs comptes compte pour une).</summary>
    public long LecturesOperations => Interlocked.Read(ref _lecturesOperations);

    internal void CompterLectureOperations() => Interlocked.Increment(ref _lecturesOperations);
}

/// <summary>
/// La banque, en mémoire. Elle offre ce dont les trois API ont besoin, et rien de propre à l'une d'elles :
/// des lectures unitaires ET groupées, un virement, et un abonnement aux nouvelles opérations d'un compte.
/// </summary>
public sealed class ServiceBancaire
{
    private readonly object _verrou = new();
    private readonly TimeProvider _horloge;
    private readonly Dictionary<string, Client> _clients = [];
    private readonly Dictionary<string, Compte> _comptes = [];
    private readonly Dictionary<string, List<Operation>> _operations = [];
    private readonly Dictionary<string, Virement> _virements = [];
    private readonly List<(string Numero, ChannelWriter<Operation> Ecrivain)> _abonnes = [];
    private long _numeroOperation;
    private long _numeroVirement;

    public Statistiques Statistiques { get; } = new();

    public ServiceBancaire(TimeProvider horloge)
    {
        _horloge = horloge;
        JeuDeDonnees.Charger(this);
    }

    // ─── Lectures ───

    public Client Client(string id)
    {
        lock (_verrou)
            return _clients.GetValueOrDefault(id) ?? throw new ErreurMetier(CodeErreur.ClientInconnu, $"Client « {id} » inconnu.");
    }

    public Compte Compte(string numero)
    {
        lock (_verrou)
            return _comptes.GetValueOrDefault(numero) ?? throw new ErreurMetier(CodeErreur.CompteInconnu, $"Compte « {numero} » inconnu.");
    }

    public IReadOnlyList<Compte> ComptesDe(string clientId)
    {
        lock (_verrou)
        {
            if (!_clients.ContainsKey(clientId))
                throw new ErreurMetier(CodeErreur.ClientInconnu, $"Client « {clientId} » inconnu.");
            return _comptes.Values.Where(c => c.ClientId == clientId).OrderBy(c => c.Numero).ToList();
        }
    }

    /// <summary>Les dernières opérations d'un compte, de la plus récente à la plus ancienne. Une requête « en base ».</summary>
    public IReadOnlyList<Operation> DernieresOperations(string numero, int nombre) =>
        DernieresOperations([numero], nombre)[numero];

    /// <summary>
    /// La même chose pour plusieurs comptes, en UNE requête « en base » (un « WHERE numero IN (…) ») : c'est ce
    /// qu'appelle le chargeur groupé de GraphQL, et le service gRPC qui construit l'écran d'accueil d'un coup.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Operation>> DernieresOperations(IReadOnlyCollection<string> numeros, int nombre)
    {
        if (nombre is < 1 or > 50)
            throw new ErreurMetier(CodeErreur.ParametreInvalide, "Le nombre d'opérations demandé doit être entre 1 et 50.");
        lock (_verrou)
        {
            Statistiques.CompterLectureOperations();
            var resultat = new Dictionary<string, IReadOnlyList<Operation>>();
            foreach (var numero in numeros.Distinct())
            {
                if (!_operations.TryGetValue(numero, out var operations))
                    throw new ErreurMetier(CodeErreur.CompteInconnu, $"Compte « {numero} » inconnu.");
                resultat[numero] = operations.AsEnumerable().Reverse().Take(nombre).ToList();
            }
            return resultat;
        }
    }

    public Virement Virement(string id)
    {
        lock (_verrou)
            return _virements.GetValueOrDefault(id) ?? throw new ErreurMetier(CodeErreur.VirementInconnu, $"Virement « {id} » inconnu.");
    }

    // ─── Écriture ───

    public Virement EffectuerVirement(string source, string cible, decimal montant, string? libelle)
    {
        if (montant <= 0 || decimal.Round(montant, 2) != montant)
            throw new ErreurMetier(CodeErreur.MontantInvalide, "Le montant doit être positif, au centime près.");
        if (source == cible)
            throw new ErreurMetier(CodeErreur.MemeCompte, "Les comptes source et cible doivent être différents.");
        libelle = string.IsNullOrWhiteSpace(libelle) ? "Virement" : libelle.Trim();

        lock (_verrou)
        {
            var compteSource = Compte(source);
            var compteCible = Compte(cible);
            if (compteSource.Solde + compteSource.PlafondDecouvert < montant)
                throw new ErreurMetier(CodeErreur.SoldeInsuffisant,
                    $"Solde insuffisant : {compteSource.Solde:0.00} € disponibles (découvert autorisé : {compteSource.PlafondDecouvert:0.00} €).");

            // À la seconde : chaque API a sa précision (ticks en JSON, millisecondes en gRPC, secondes dans le schéma
            // GraphQL) ; sans cet arrondi, le même écran différait d'une API à l'autre (vu à l'essai réel).
            var maintenant = _horloge.GetUtcNow();
            maintenant = maintenant.AddTicks(-(maintenant.Ticks % TimeSpan.TicksPerSecond));
            var id = $"V{++_numeroVirement:000}";
            Ajouter(compteSource, maintenant, $"{libelle} vers {cible}", -montant);
            Ajouter(compteCible, maintenant, $"{libelle} de {source}", montant);
            var virement = new Virement(id, source, cible, montant, libelle, maintenant, _comptes[source].Solde);
            _virements[id] = virement;
            return virement;
        }
    }

    // ─── Abonnement (ce que sert le flux gRPC) ───

    /// <summary>Les opérations du compte à partir de maintenant, au fil de l'eau, jusqu'à l'annulation.</summary>
    public ChannelReader<Operation> Suivre(string numero, CancellationToken annulation)
    {
        var canal = Channel.CreateUnbounded<Operation>();
        lock (_verrou)
        {
            Compte(numero);
            _abonnes.Add((numero, canal.Writer));
        }
        annulation.Register(() =>
        {
            lock (_verrou)
                _abonnes.RemoveAll(a => a.Ecrivain == canal.Writer);
            canal.Writer.TryComplete();
        });
        return canal.Reader;
    }

    public int NombreAbonnes
    {
        get { lock (_verrou) return _abonnes.Count; }
    }

    // ─── Chargement ───

    internal void AjouterClient(Client client) => _clients[client.Id] = client;

    internal void AjouterCompte(Compte compte)
    {
        _comptes[compte.Numero] = compte with { Solde = 0, Version = 0 };
        _operations[compte.Numero] = [];
    }

    internal void AjouterOperation(string numero, DateTimeOffset date, string libelle, decimal montant) =>
        Ajouter(_comptes[numero], date, libelle, montant);

    private void Ajouter(Compte compte, DateTimeOffset date, string libelle, decimal montant)
    {
        var operation = new Operation($"OP{++_numeroOperation:0000}", compte.Numero, date, libelle, montant);
        _operations[compte.Numero].Add(operation);
        _comptes[compte.Numero] = compte with { Solde = compte.Solde + montant, Version = compte.Version + 1 };
        foreach (var abonne in _abonnes.Where(a => a.Numero == compte.Numero))
            abonne.Ecrivain.TryWrite(operation);
    }
}

using System.Collections.Concurrent;

namespace Espace.Grille;

public sealed class OptionsPlateforme
{
    public int CapaciteParUnite { get; set; } = 4;
    public int UnitesAuDemarrage { get; set; } = 2;
    public int UnitesMin { get; set; } = 1;
    public int UnitesMax { get; set; } = 6;

    /// <summary>Charge moyenne (requêtes en cours ou en attente / capacité) au-delà de laquelle on ajoute une unité.</summary>
    public double SeuilHaut { get; set; } = 0.75;

    /// <summary>Charge moyenne en deçà de laquelle on en retire une.</summary>
    public double SeuilBas { get; set; } = 0.25;

    public int LatenceReplicationMs { get; set; }
    public int LatenceBaseMs { get; set; }
    public int TempsDeTraitementMs { get; set; }
}

public sealed record EtatUnite(string Id, int EnCours, int EnAttente, long Traitees, int Entrees);

public sealed record EtatPlateforme(
    ModeRoutage Routage,
    IReadOnlyList<EtatUnite> Unites,
    int ReplicationEnAttente,
    int MisesAJourPerdues,
    int PompeEnAttente,
    bool BaseDisponible,
    long EcrituresBase,
    long LecturesBase,
    int DemarragesAFroid);

/// <summary>
/// L'« intergiciel virtualisé » assemblé : grille de données, grille de messages, pompe, base, gestionnaire de déploiement.
/// Le métier n'y apporte qu'une fonction : traiter une requête dans le contexte d'une unité.
/// </summary>
public sealed class Plateforme : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _journal = new();

    public Plateforme(OptionsPlateforme options, Func<object, ContexteUnite, Task<object>> traitement)
    {
        Options = options;
        Base = new Base { Latence = TimeSpan.FromMilliseconds(options.LatenceBaseMs) };
        Pompe = new PompeDeDonnees(Base);
        Donnees = new GrilleDeDonnees(Pompe.Deposer, Noter) { LatenceReplication = TimeSpan.FromMilliseconds(options.LatenceReplicationMs) };
        Simulation = new SimulationCharge { TempsDeTraitement = TimeSpan.FromMilliseconds(options.TempsDeTraitementMs) };
        Messages = new GrilleDeMessages();
        Deploiement = new GestionnaireDeDeploiement(this, traitement);
    }

    public OptionsPlateforme Options { get; }
    public Base Base { get; }
    public PompeDeDonnees Pompe { get; }
    public GrilleDeDonnees Donnees { get; }
    public GrilleDeMessages Messages { get; }
    public SimulationCharge Simulation { get; }
    public GestionnaireDeDeploiement Deploiement { get; }

    /// <summary>Remplit la base, puis démarre les premières unités (la première à froid, depuis la base).</summary>
    public async Task Demarrer(IEnumerable<Entree> donneesInitiales)
    {
        Base.Initialiser(donneesInitiales);
        for (var i = 0; i < Options.UnitesAuDemarrage; i++)
            await Deploiement.Demarrer();
    }

    public Task<(object Reponse, string Unite)> Envoyer(IRequete requete) => Messages.Envoyer(requete);

    /// <summary>Attend que tout soit au repos : répliques identiques, base à jour.</summary>
    public async Task AttendreStabilisation()
    {
        await Donnees.AttendreReplication();
        await Pompe.AttendreVidage();
    }

    public EtatPlateforme Etat() => new(
        Messages.Mode,
        Messages.Unites().Select(u => new EtatUnite(u.Id, u.EnCours, u.EnAttente, u.Traitees, u.Replique.Nombre)).ToList(),
        Donnees.EnAttente, Donnees.MisesAJourPerdues, Pompe.EnAttente, Base.Disponible, Base.Ecritures, Base.Lectures,
        Deploiement.DemarragesAFroid);

    /// <summary>Les derniers événements (unités démarrées ou arrêtées, collisions), du plus récent au plus ancien.</summary>
    public IReadOnlyList<string> Journal() => _journal.Reverse().ToList();

    internal void Noter(string evenement)
    {
        _journal.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {evenement}");
        while (_journal.Count > 200)
            _journal.TryDequeue(out _);
    }

    public async ValueTask DisposeAsync()
    {
        Simulation.Ouvrir();
        await Donnees.DisposeAsync();
        await Pompe.DisposeAsync();
    }
}

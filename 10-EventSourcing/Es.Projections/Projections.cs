using Es.Domaine;
using Es.Stockage;

namespace Es.Projections;

public interface IProjection
{
    void Appliquer(EvenementStocke stocke, IEvenement evenement);
    void Vider();
}

// ─── Relevés : l'état courant de chaque compte, prêt à afficher ───

public record ReleveVue(Guid Id, string Titulaire, decimal Solde, bool Cloture, int NombreOperations, int Version);

public class ProjectionReleves : IProjection
{
    private readonly Dictionary<Guid, ReleveVue> _releves = [];

    public ReleveVue? Obtenir(Guid id) => _releves.GetValueOrDefault(id);

    public void Appliquer(EvenementStocke stocke, IEvenement evenement)
    {
        if (evenement is CompteOuvert o)
        {
            _releves[o.CompteId] = new ReleveVue(o.CompteId, o.Titulaire, o.DepotInitial, false, 1, stocke.Version);
            return;
        }

        var r = _releves[evenement.CompteId];
        var solde = evenement switch
        {
            ArgentDepose e => r.Solde + e.Montant,
            ArgentRetire e => r.Solde - e.Montant,
            VirementEmis e => r.Solde - e.Montant,
            VirementRecu e => r.Solde + e.Montant,
            _ => r.Solde
        };
        _releves[evenement.CompteId] = r with
        {
            Solde = solde,
            Cloture = r.Cloture || evenement is CompteCloture,
            NombreOperations = r.NombreOperations + 1,
            Version = stocke.Version
        };
    }

    public void Vider() => _releves.Clear();
}

// ─── Plus gros retraits : une projection ajoutée APRÈS coup ───
// Comme elle se construit en rejouant tout le flux, elle connaît d'emblée tous les retraits passés,
// y compris ceux faits avant qu'elle existe. Impossible avec une base classique, qui n'a gardé que l'état final.

public record RetraitVue(string Titulaire, decimal Montant, string Motif, DateTime Date);

public class ProjectionPlusGrosRetraits : IProjection
{
    private readonly Dictionary<Guid, string> _titulaires = [];
    private readonly List<RetraitVue> _retraits = [];

    public IReadOnlyList<RetraitVue> Top(int nombre) =>
        _retraits.OrderByDescending(r => r.Montant).ThenBy(r => r.Date).Take(nombre).ToList();

    public void Appliquer(EvenementStocke stocke, IEvenement evenement)
    {
        switch (evenement)
        {
            case CompteOuvert e:
                _titulaires[e.CompteId] = e.Titulaire;
                break;
            case ArgentRetire e:
                _retraits.Add(new RetraitVue(_titulaires[e.CompteId], e.Montant, e.Motif, e.Date));
                break;
        }
    }

    public void Vider()
    {
        _titulaires.Clear();
        _retraits.Clear();
    }
}

/// <summary>
/// Fait avancer toutes les projections sur le flux global, depuis la dernière position traitée.
/// Toutes les lectures passent par ce moteur, protégé par un verrou (les projections sont en mémoire).
/// </summary>
public class MoteurProjections(IMagasinEvenements magasin, ProjectionReleves releves, ProjectionPlusGrosRetraits retraits)
{
    private readonly IProjection[] _projections = [releves, retraits];
    private readonly SemaphoreSlim _verrou = new(1, 1);

    public long Position { get; private set; }

    public async Task<int> Rattraper()
    {
        await _verrou.WaitAsync();
        try
        {
            var nouveaux = await magasin.LireTout(Position);
            foreach (var stocke in nouveaux)
            {
                var evenement = stocke.Evenement();
                foreach (var projection in _projections)
                    projection.Appliquer(stocke, evenement);
                Position = stocke.Position;
            }
            return nouveaux.Count;
        }
        finally
        {
            _verrou.Release();
        }
    }

    /// <summary>Efface toutes les projections et rejoue tout l'historique depuis le premier événement.</summary>
    public async Task<int> Reconstruire()
    {
        await _verrou.WaitAsync();
        try
        {
            foreach (var projection in _projections)
                projection.Vider();
            Position = 0;
        }
        finally
        {
            _verrou.Release();
        }
        return await Rattraper();
    }

    public async Task<T> Lire<T>(Func<T> lecture)
    {
        await _verrou.WaitAsync();
        try
        {
            return lecture();
        }
        finally
        {
            _verrou.Release();
        }
    }
}

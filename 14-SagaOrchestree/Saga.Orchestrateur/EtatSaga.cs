using System.Collections.Concurrent;
using System.Text.Json;

namespace Saga.Orchestrateur;

public enum StatutSaga
{
    EnCours,
    EnAttente,             // attend un événement extérieur (décision d'un analyste), avec une échéance
    Compensation,          // défait les étapes réussies, de la plus récente à la plus ancienne
    Terminee,
    Annulee,               // tout a été défait proprement
    ATraiterManuellement   // échec APRÈS le point de non-retour : un humain doit intervenir
}

public record EntreeJournal(DateTime Date, string Message);

/// <summary>
/// L'état complet d'une saga : c'est TOUT ce dont l'orchestrateur a besoin pour reprendre.
/// Il est enregistré après chaque transition ; l'orchestrateur lui-même ne garde rien en mémoire.
/// </summary>
public sealed class EtatSaga
{
    public Guid Id { get; set; }
    public Guid Source { get; set; }
    public Guid Cible { get; set; }
    public decimal Montant { get; set; }

    public StatutSaga Statut { get; set; }
    public int EtapeCourante { get; set; }
    public List<string> EtapesFaites { get; set; } = [];
    public int Tentatives { get; set; }
    public DateTime ProchainEssai { get; set; }
    public DateTime? Echeance { get; set; }
    public string? Raison { get; set; }

    // Données produites par les étapes et utilisées par les suivantes.
    public string? DeviseCible { get; set; }
    public decimal? MontantCredite { get; set; }

    public List<EntreeJournal> Journal { get; set; } = [];

    public bool EstFinie => Statut is StatutSaga.Terminee or StatutSaga.Annulee or StatutSaga.ATraiterManuellement;
}

/// <summary>
/// Stockage de l'état des sagas. Les états sont conservés SÉRIALISÉS (comme dans une base) :
/// chaque lecture produit une copie neuve, aucun objet n'est partagé avec l'orchestrateur.
/// </summary>
public sealed class MagasinSagas
{
    private readonly ConcurrentDictionary<Guid, string> _sagas = new();

    public void Enregistrer(EtatSaga saga) => _sagas[saga.Id] = JsonSerializer.Serialize(saga);

    public EtatSaga? Charger(Guid id) =>
        _sagas.TryGetValue(id, out var json) ? JsonSerializer.Deserialize<EtatSaga>(json) : null;

    public IReadOnlyList<EtatSaga> Toutes() =>
        _sagas.Values.Select(json => JsonSerializer.Deserialize<EtatSaga>(json)!).ToList();
}

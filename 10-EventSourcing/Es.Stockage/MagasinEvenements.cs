using System.Text.Encodings.Web;
using System.Text.Json;
using Es.Domaine;

namespace Es.Stockage;

/// <summary>Un événement tel qu'il est stocké : sa position globale, son flux (le compte) et sa version dans ce flux.</summary>
public record EvenementStocke(long Position, Guid Flux, int Version, string Type, string Donnees, DateTime Date)
{
    public IEvenement Evenement() => SerialiseurEvenements.Deserialiser(Type, Donnees);
}

/// <summary>Événements à ajouter à un flux, à condition que le flux soit encore à la version attendue.</summary>
public record AEcrire(Guid Flux, int VersionAttendue, IReadOnlyList<IEvenement> Evenements);

public class ConflitDeConcurrenceException(Guid flux, int versionAttendue, int versionActuelle)
    : Exception($"Le compte {flux} a été modifié entre-temps (version attendue {versionAttendue}, version actuelle {versionActuelle}).");

public interface IMagasinEvenements
{
    Task<IReadOnlyList<EvenementStocke>> LireFlux(Guid flux, int apresVersion = 0);
    Task<IReadOnlyList<EvenementStocke>> LireTout(long apresPosition = 0, int maximum = int.MaxValue);
    Task Ajouter(params AEcrire[] ecritures);
}

/// <summary>
/// Magasin en ajout seul : on n'y modifie ni n'y supprime jamais rien.
/// Avec un fichier, chaque événement y est ajouté en une ligne JSON ; au redémarrage, tout est relu.
/// </summary>
public sealed class MagasinEvenements : IMagasinEvenements
{
    private readonly List<EvenementStocke> _evenements = [];
    private readonly string? _fichier;
    private readonly Lock _verrou = new();

    // Format d'une ligne du fichier : les données de l'événement sont écrites en JSON lisible, pas en chaîne échappée.
    private record Ligne(long Position, Guid Flux, int Version, string Type, JsonElement Donnees, DateTime Date);

    private static readonly JsonSerializerOptions FormatFichier = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public MagasinEvenements(string? fichier = null)
    {
        _fichier = fichier;
        if (fichier is not null && File.Exists(fichier))
            _evenements.AddRange(File.ReadLines(fichier)
                .Where(ligne => !string.IsNullOrWhiteSpace(ligne))
                .Select(ligne => JsonSerializer.Deserialize<Ligne>(ligne, FormatFichier)!)
                .Select(l => new EvenementStocke(l.Position, l.Flux, l.Version, l.Type, l.Donnees.GetRawText(), l.Date)));
    }

    private static string VersLigne(EvenementStocke e) => JsonSerializer.Serialize(
        new Ligne(e.Position, e.Flux, e.Version, e.Type, JsonSerializer.Deserialize<JsonElement>(e.Donnees), e.Date),
        FormatFichier);

    public Task<IReadOnlyList<EvenementStocke>> LireFlux(Guid flux, int apresVersion = 0)
    {
        lock (_verrou)
            return Task.FromResult<IReadOnlyList<EvenementStocke>>(
                _evenements.Where(e => e.Flux == flux && e.Version > apresVersion).ToList());
    }

    public Task<IReadOnlyList<EvenementStocke>> LireTout(long apresPosition = 0, int maximum = int.MaxValue)
    {
        lock (_verrou)
            return Task.FromResult<IReadOnlyList<EvenementStocke>>(
                _evenements.Where(e => e.Position > apresPosition).Take(maximum).ToList());
    }

    /// <summary>
    /// Ajoute les événements de un ou plusieurs flux, en tout ou rien.
    /// Concurrence optimiste : si un flux a bougé depuis son chargement, rien n'est écrit.
    /// </summary>
    public Task Ajouter(params AEcrire[] ecritures)
    {
        lock (_verrou)
        {
            foreach (var ecriture in ecritures)
            {
                var actuelle = _evenements.Count(e => e.Flux == ecriture.Flux);
                if (actuelle != ecriture.VersionAttendue)
                    throw new ConflitDeConcurrenceException(ecriture.Flux, ecriture.VersionAttendue, actuelle);
            }

            var nouveaux = new List<EvenementStocke>();
            foreach (var ecriture in ecritures)
            {
                var version = ecriture.VersionAttendue;
                foreach (var evenement in ecriture.Evenements)
                    nouveaux.Add(new EvenementStocke(
                        _evenements.Count + nouveaux.Count + 1, ecriture.Flux, ++version,
                        SerialiseurEvenements.NomType(evenement), SerialiseurEvenements.Serialiser(evenement), evenement.Date));
            }

            if (_fichier is not null)
                File.AppendAllLines(_fichier, nouveaux.Select(VersLigne));
            _evenements.AddRange(nouveaux);
        }
        return Task.CompletedTask;
    }
}

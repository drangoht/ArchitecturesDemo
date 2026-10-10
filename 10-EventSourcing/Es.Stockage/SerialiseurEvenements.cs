using System.Text.Json;
using System.Text.Json.Nodes;
using Es.Domaine;

namespace Es.Stockage;

/// <summary>
/// Traduit les événements en JSON et inversement. Les noms de types stockés sont fixés ici, une fois pour toutes :
/// renommer une classe C# ne doit jamais rendre illisible l'historique.
/// </summary>
public static class SerialiseurEvenements
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> Types = new()
    {
        ["CompteOuvert"] = typeof(CompteOuvert),
        ["ArgentDepose"] = typeof(ArgentDepose),
        ["ArgentRetire.v2"] = typeof(ArgentRetire),
        ["VirementEmis"] = typeof(VirementEmis),
        ["VirementRecu"] = typeof(VirementRecu),
        ["CompteCloture"] = typeof(CompteCloture)
    };

    private static readonly Dictionary<Type, string> Noms = Types.ToDictionary(p => p.Value, p => p.Key);

    /// <summary>
    /// Upcasters : convertissent un ancien format vers le format actuel, au moment de la lecture.
    /// L'historique n'est jamais réécrit : un événement stocké est immuable.
    /// </summary>
    private static readonly Dictionary<string, Func<string, (string Type, string Json)>> Upcasters = new()
    {
        // v1 : { compteId, montant, date }  →  v2 : + motif
        ["ArgentRetire"] = json =>
        {
            var noeud = JsonNode.Parse(json)!.AsObject();
            noeud["motif"] = "Non précisé";
            return ("ArgentRetire.v2", noeud.ToJsonString());
        }
    };

    public static string NomType(IEvenement evenement) => Noms[evenement.GetType()];

    public static string Serialiser(IEvenement evenement) => JsonSerializer.Serialize(evenement, evenement.GetType(), Json);

    public static IEvenement Deserialiser(string type, string json)
    {
        while (Upcasters.TryGetValue(type, out var upcaster))
            (type, json) = upcaster(json);
        return (IEvenement)JsonSerializer.Deserialize(json, Types[type], Json)!;
    }
}

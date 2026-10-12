using System.Text.Json;
using System.Text.Json.Nodes;
using Es.Domain;

namespace Es.Storage;

/// <summary>
/// Translates the events into JSON and back. The stored type names are fixed here, once and for all:
/// renaming a C# class must never make the history unreadable.
/// </summary>
public static class EventSerializer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> Types = new()
    {
        ["AccountOpened"] = typeof(AccountOpened),
        ["MoneyDeposited"] = typeof(MoneyDeposited),
        ["MoneyWithdrawn.v2"] = typeof(MoneyWithdrawn),
        ["TransferSent"] = typeof(TransferSent),
        ["TransferReceived"] = typeof(TransferReceived),
        ["AccountClosed"] = typeof(AccountClosed)
    };

    private static readonly Dictionary<Type, string> Names = Types.ToDictionary(p => p.Value, p => p.Key);

    /// <summary>
    /// Upcasters: convert an old format to the current format, at read time.
    /// The history is never rewritten: a stored event is immutable.
    /// </summary>
    private static readonly Dictionary<string, Func<string, (string Type, string Json)>> Upcasters = new()
    {
        // v1: { accountId, amount, date }  →  v2: + reason
        ["MoneyWithdrawn"] = json =>
        {
            var node = JsonNode.Parse(json)!.AsObject();
            node["reason"] = "Not specified";
            return ("MoneyWithdrawn.v2", node.ToJsonString());
        }
    };

    public static string TypeName(IEvent @event) => Names[@event.GetType()];

    public static string Serialize(IEvent @event) => JsonSerializer.Serialize(@event, @event.GetType(), Json);

    public static IEvent Deserialize(string type, string json)
    {
        while (Upcasters.TryGetValue(type, out var upcaster))
            (type, json) = upcaster(json);
        return (IEvent)JsonSerializer.Deserialize(json, Types[type], Json)!;
    }
}

using System.Collections.Concurrent;
using System.Text.Json;

namespace Actors.Bank;

/// <summary>
/// The DURABLE storage of the actors (a database, for real): one row per actor, written by it alone.
/// The state is serialized as JSON, hence copied: what an actor has in memory and what is stored are two distinct things.
/// As long as it has not written, a change only exists in its incarnation, and disappears with it if it crashes.
/// </summary>
public sealed class StateStore
{
    private readonly ConcurrentDictionary<string, string> _rows = new();

    public Task<T?> Read<T>(string path) where T : class =>
        Task.FromResult(_rows.TryGetValue(path, out var json) ? JsonSerializer.Deserialize<T>(json) : null);

    public Task Write<T>(string path, T state)
    {
        _rows[path] = JsonSerializer.Serialize(state);
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<string> Paths() => _rows.Keys.Order().ToList();
}

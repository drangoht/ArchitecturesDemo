using System.Text.Json;
using Hexa.Hexagon.Domain;
using Hexa.Hexagon.Ports.Outbound;

namespace Hexa.Adapters.Persistence;

/// <summary>
/// Outbound adapter that stores the accounts in a JSON file.
/// Same port as <see cref="InMemoryAccounts"/>: the hexagon sees no difference.
/// </summary>
public class JsonFileAccounts : IAccounts
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonFileAccounts(string path, IEnumerable<Account>? accountsIfFileMissing = null)
    {
        _path = path;
        if (!File.Exists(path))
            Write((accountsIfFileMissing ?? Enumerable.Empty<Account>()).Select(Row.From).ToList());
    }

    public async Task<Account?> Load(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            return Read().FirstOrDefault(l => l.Id == id)?.ToAccount();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task Save(Account account)
    {
        await _lock.WaitAsync();
        try
        {
            var rows = Read();
            rows.RemoveAll(l => l.Id == account.Id);
            rows.Add(Row.From(account));
            Write(rows);
        }
        finally
        {
            _lock.Release();
        }
    }

    private List<Row> Read() =>
        JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(_path)) ?? [];

    private void Write(List<Row> rows) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(rows.OrderBy(l => l.Owner), Options));

    // Storage format specific to the adapter: the domain does not know it is serialized.
    private record Row(Guid Id, string Owner, decimal Balance)
    {
        public static Row From(Account account) => new(account.Id, account.Owner, account.Balance);
        public Account ToAccount() => new(Id, Owner, Balance);
    }
}

namespace Simple.Data;

public interface IRow
{
    Guid Id { get; }
}

// The rows are plain data, like the columns of a table: no rule, no behaviour.
public record AccountRow(Guid Id, string Owner, decimal Balance) : IRow;

public record OperationRow(Guid Id, Guid AccountId, string Label, decimal Amount, DateTime DateUtc) : IRow;

/// <summary>The four statements the demos need: SELECT by key, SELECT … WHERE, INSERT, UPDATE.</summary>
public sealed class Table<TRow>(string name, Dictionary<Guid, TRow> rows) where TRow : class, IRow
{
    internal Dictionary<Guid, TRow> Rows => rows;

    public TRow? Find(Guid id) => rows.GetValueOrDefault(id);

    public IReadOnlyList<TRow> Where(Func<TRow, bool> predicate) => [.. rows.Values.Where(predicate)];

    public void Insert(TRow row)
    {
        if (!rows.TryAdd(row.Id, row))
            throw new InvalidOperationException($"{name}: the key {row.Id} already exists.");
    }

    public void Update(TRow row)
    {
        if (!rows.ContainsKey(row.Id))
            throw new InvalidOperationException($"{name}: no row with the key {row.Id}.");
        rows[row.Id] = row;
    }
}

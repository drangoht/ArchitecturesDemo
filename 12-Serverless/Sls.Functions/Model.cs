using System.Security.Cryptography;
using System.Text;
using Sls.Platform;

namespace Sls.Functions;

public class BusinessRuleException(string message) : Exception(message);

/// <param name="Operations">
/// Latest applied operations: a replayed movement (retry, double delivery) is recognized and ignored.
/// </param>
public record Account(Guid Id, string Owner, decimal Balance, Guid[] Operations)
{
    public Account Apply(Guid operation, decimal change)
    {
        if (Operations.Contains(operation))
            return this;
        if (Balance + change < 0)
            throw new BusinessRuleException("Insufficient balance.");
        return this with { Balance = Balance + change, Operations = [.. Operations.TakeLast(49), operation] };
    }
}

public record Transfer(Guid Id, Guid Source, Guid Target, decimal Amount, string Status, string? Error = null);

public record Notification(Guid AccountId, string Message, DateTime Date);

public record Counter(long Value);

internal static class Concurrency
{
    /// <summary>
    /// Read → modify → write "only if nobody wrote in the meantime", and start over otherwise.
    /// Essential: dozens of instances of the same function can touch the same data at the same time.
    /// </summary>
    public static async Task<T> Modify<T>(ManagedTable table, string key, Func<T, T> modification) where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            var (value, etag) = await table.Read<T>(key);
            if (value is null)
                throw new KeyNotFoundException($"{table.Name}/{key}");
            try
            {
                var updated = modification(value);
                await table.Write(key, updated, etag);
                return updated;
            }
            catch (WriteConflictException) when (attempt < 20)
            {
                // Another instance wrote before us: read again and start over.
            }
        }
    }

    public static async Task<T> ModifyOrCreate<T>(ManagedTable table, string key, T initial, Func<T, T> modification) where T : class
    {
        try
        {
            await table.Write(key, initial, expectedEtag: 0);
        }
        catch (WriteConflictException)
        {
            // It already existed.
        }
        return await Modify(table, key, modification);
    }

    /// <summary>Same input → same identifier: lets a processing be replayed without creating duplicates.</summary>
    public static Guid DeterministicId(string text) => new(MD5.HashData(Encoding.UTF8.GetBytes(text)));
}

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static void Load(ManagedServices services)
    {
        var accounts = services.Table(Tables.Accounts);
        accounts.Write(Alice.ToString(), new Account(Alice, "Alice", 1_000m, []));
        accounts.Write(Bob.ToString(), new Account(Bob, "Bob", 500m, []));
        accounts.Write(Carla.ToString(), new Account(Carla, "Carla", 5_000m, []));
    }
}

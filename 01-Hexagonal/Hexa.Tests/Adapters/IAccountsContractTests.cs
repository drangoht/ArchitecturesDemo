using Hexa.Adapters.Persistence;
using Hexa.Hexagon.Domain;
using Hexa.Hexagon.Ports.Outbound;

namespace Hexa.Tests.Adapters;

/// <summary>
/// Contract tests: the same tests run against every adapter of the IAccounts port.
/// This is what guarantees that one adapter can replace another without surprises.
/// </summary>
public abstract class IAccountsContract
{
    protected abstract IAccounts Create(params Account[] initialAccounts);

    [Fact]
    public async Task Loading_an_unknown_account_returns_null()
    {
        Assert.Null(await Create().Load(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_saved_account_is_read_back_identically()
    {
        var id = Guid.NewGuid();
        var accounts = Create(new Account(id, "Alice", 100m));

        var account = (await accounts.Load(id))!;
        account.Withdraw(40m);
        await accounts.Save(account);

        var reread = (await accounts.Load(id))!;
        Assert.Equal("Alice", reread.Owner);
        Assert.Equal(60m, reread.Balance);
    }

    [Fact]
    public async Task An_unsaved_change_is_not_persisted()
    {
        var id = Guid.NewGuid();
        var accounts = Create(new Account(id, "Alice", 100m));

        (await accounts.Load(id))!.Withdraw(40m);

        Assert.Equal(100m, (await accounts.Load(id))!.Balance);
    }
}

public class InMemoryAccountsTests : IAccountsContract
{
    protected override IAccounts Create(params Account[] initialAccounts) => new InMemoryAccounts(initialAccounts);
}

public class JsonFileAccountsTests : IAccountsContract, IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"accounts-{Guid.NewGuid()}.json");

    protected override IAccounts Create(params Account[] initialAccounts) => new JsonFileAccounts(_file, initialAccounts);

    public void Dispose() => File.Delete(_file);
}

using Onion.Application;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;
using Onion.Domain.Services;

namespace Onion.Tests.Application;

// The application layer is tested with a fake repository / unit of work: no EF Core here.
public class TransfersAppServiceTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly FakeRepository _accounts = new(new Account(Alice, "Alice", 100m), new Account(Bob, "Bob", 0m));
    private readonly FakeUnitOfWork _unitOfWork = new();

    private TransfersAppService Service => new(_accounts, _unitOfWork, new TransferService());

    [Fact]
    public async Task A_successful_transfer_is_saved_only_once()
    {
        var result = await Service.Transfer(Alice, Bob, 30m);

        Assert.Equal(70m, result.Source.Balance);
        Assert.Equal(30m, result.Target.Balance);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_refused_transfer_is_not_saved()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.Transfer(Alice, Bob, 1_000m));

        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_unknown_account_throws_AccountNotFound()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(() => Service.Transfer(Alice, Guid.NewGuid(), 10m));
    }

    private class FakeRepository(params Account[] accounts) : IAccountsRepository
    {
        private readonly List<Account> _accounts = [.. accounts];
        public Task<Account?> ById(Guid id) => Task.FromResult(_accounts.FirstOrDefault(c => c.Id == id));
        public Task Add(Account account) { _accounts.Add(account); return Task.CompletedTask; }
        public Task<bool> IsEmpty() => Task.FromResult(_accounts.Count == 0);
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task Save() { SaveCount++; return Task.CompletedTask; }
    }
}

using Clean.InterfaceAdapters.Gateways;
using Clean.UseCases.Withdrawal;
using Clean.Entities;

namespace Clean.Tests.UseCases;

// The interactor is tested with a spy on the output boundary: we check WHAT IT SAYS, not a format.
public class WithdrawalInteractorTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private readonly InMemoryAccountsGateway _accounts = new(new Account(Alice, "Alice", 100m));
    private readonly SpyOutput _output = new();

    [Fact]
    public async Task A_valid_withdrawal_reports_the_old_and_new_balance()
    {
        await new WithdrawalInteractor(_accounts).Execute(new WithdrawalRequest(Alice, 30m), _output);

        Assert.Equal(new WithdrawalResponse(Alice, 100m, 70m), _output.Success);
        Assert.Equal(70m, (await _accounts.Load(Alice))!.Balance);
    }

    [Fact]
    public async Task A_withdrawal_that_is_too_large_reports_a_refusal()
    {
        await new WithdrawalInteractor(_accounts).Execute(new WithdrawalRequest(Alice, 500m), _output);

        Assert.Equal("Insufficient balance.", _output.Refused);
        Assert.Null(_output.Success);
    }

    [Fact]
    public async Task An_unknown_account_is_reported_as_not_found()
    {
        var unknown = Guid.NewGuid();

        await new WithdrawalInteractor(_accounts).Execute(new WithdrawalRequest(unknown, 10m), _output);

        Assert.Equal(unknown, _output.NotFound);
    }

    private class SpyOutput : IWithdrawalOutput
    {
        public WithdrawalResponse? Success { get; private set; }
        public Guid? NotFound { get; private set; }
        public string? Refused { get; private set; }

        void IWithdrawalOutput.Success(WithdrawalResponse response) => Success = response;
        void IWithdrawalOutput.AccountNotFound(Guid accountId) => NotFound = accountId;
        void IWithdrawalOutput.Refused(string reason) => Refused = reason;
    }
}

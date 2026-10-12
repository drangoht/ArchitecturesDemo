using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Modular.Accounts;
using Modular.Accounts.Contracts;
using Modular.Shared;
using Modular.Transfers.Contracts;

namespace Modular.Tests;

// The modules only know each other through contracts: one can be replaced by a fake.
public class IsolationTests
{
    [Fact]
    public async Task The_Transfers_module_compensates_if_the_credit_fails()
    {
        var fake = new FakeAccounts();
        await using var app = new BankApplication(services => services.AddScoped<IAccountsApi>(_ => fake));

        var response = await app.CreateClient().PostAsJsonAsync("/transfers",
            new { source = FakeAccounts.Source, target = FakeAccounts.Target, amount = 40m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["debit source 40", "credit target 40 (refused)", "credit source 40"], fake.Log);
    }

    [Fact]
    public async Task A_failure_in_a_subscriber_does_not_break_the_publishing_module()
    {
        await using var app = new BankApplication(services =>
            services.AddScoped<IEventHandler<TransferMade>, FailingHandler>());
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers",
            new { source = AccountsModule.Alice, target = AccountsModule.Bob, amount = 10m });

        response.EnsureSuccessStatusCode();
        // The Notifications handler still did its job.
        var notifications = await client.GetFromJsonAsync<JsonElement>($"/notifications/{AccountsModule.Bob}");
        Assert.Equal(1, notifications.GetArrayLength());
    }

    private class FailingHandler : IEventHandler<TransferMade>
    {
        public Task Handle(TransferMade @event) => throw new InvalidOperationException("Simulated failure");
    }

    private class FakeAccounts : IAccountsApi
    {
        public static readonly Guid Source = Guid.NewGuid();
        public static readonly Guid Target = Guid.NewGuid();

        public List<string> Log { get; } = [];

        public Task<AccountInfo?> Get(Guid accountId) =>
            Task.FromResult<AccountInfo?>(new AccountInfo(accountId, Name(accountId), 100m));

        public Task<OperationResult> Debit(Guid accountId, decimal amount)
        {
            Log.Add($"debit {Name(accountId)} {amount}");
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> Credit(Guid accountId, decimal amount)
        {
            if (accountId == Target)
            {
                Log.Add($"credit target {amount} (refused)");
                return Task.FromResult(OperationResult.Refused("Target account blocked."));
            }
            Log.Add($"credit {Name(accountId)} {amount}");
            return Task.FromResult(OperationResult.Ok());
        }

        private static string Name(Guid id) => id == Source ? "source" : "target";
    }
}

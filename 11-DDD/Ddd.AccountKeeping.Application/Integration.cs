using Ddd.AccountKeeping.Contracts;
using Ddd.AccountKeeping.Domain;
using Ddd.AccountKeeping.Domain.Accounts;
using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Application;

/// <summary>
/// Translates the DOMAIN events (internal, rich) into INTEGRATION events (published language, simple),
/// then notifies the other contexts. The internal model never leaks outside.
/// </summary>
/// <param name="subscribers">Resolved at publication time (and not at construction) to avoid a dependency cycle.</param>
public class EventPublisher(Func<IEnumerable<IAccountKeepingSubscriber>> subscribers, ICurrencyConverter converter)
{
    public async Task Publish(IEnumerable<IDomainEvent> events)
    {
        foreach (var @event in events)
        {
            if (@event is not TransferMade v)
                continue;

            var inEuros = v.DebitedAmount.Currency == Currency.Euro
                ? v.DebitedAmount
                : await converter.Convert(v.DebitedAmount, Currency.Euro);

            var integration = new TransferSent(
                v.TransferId, v.Source.Value, v.SourceIban.Value, v.SourceOwner.Value, v.TargetIban.Value,
                v.DebitedAmount.Amount, v.DebitedAmount.Currency.Code, inEuros.Amount, v.Date);

            foreach (var subscriber in subscribers())
                await subscriber.OnTransferSent(integration);
        }
    }
}

/// <summary>Implementation of the open host service: what the other contexts can ask for.</summary>
public class AccountKeepingApi(AccountsService accounts) : IAccountKeepingApi
{
    public Task BlockAccount(Guid accountId, string reason) => accounts.Block(accountId, reason);
}

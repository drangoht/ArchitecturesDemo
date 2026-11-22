using Med.Bank.Data;
using Med.Bank.Domain;
using Med.Bank.Pipeline;
using Med.Mediator;
using Microsoft.EntityFrameworkCore;

namespace Med.Bank.Features.Accounts;

public record GetAccount(Guid AccountId) : IQuery<AccountView>;

public record AccountView(Guid Id, string Owner, decimal Balance, IReadOnlyList<OperationView> LatestOperations,
    IReadOnlyList<string> Notifications);

public record OperationView(string Label, decimal Amount);

/// <summary>A query reads the same tables as the commands: the mediator does not separate the models.</summary>
internal class GetAccountHandler(BankDb db) : IRequestHandler<GetAccount, AccountView>
{
    public async Task<AccountView> Handle(GetAccount request, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.AccountId, ct)
            ?? throw new NotFoundException($"Account {request.AccountId} not found.");

        var operations = await db.Operations.AsNoTracking()
            .Where(o => o.AccountId == request.AccountId)
            .OrderByDescending(o => o.DateUtc)
            .Take(5)
            .Select(o => new OperationView(o.Label, o.Amount))
            .ToListAsync(ct);
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.AccountId == request.AccountId)
            .Select(n => n.Message)
            .ToListAsync(ct);

        return new AccountView(account.Id, account.Owner, account.Balance, operations, notifications);
    }
}

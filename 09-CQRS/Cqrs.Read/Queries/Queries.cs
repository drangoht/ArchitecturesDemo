using Cqrs.Read.Model;
using Microsoft.EntityFrameworkCore;

namespace Cqrs.Read.Queries;

// The query handlers read the views as they are: no domain, no join, no computation.

public record OperationView(string Label, decimal Amount, decimal BalanceAfter, DateTime Date);

// ─── Statement of an account ───

public record GetStatement(Guid AccountId) : IQuery<StatementView?>;

public record StatementView(Guid Id, string Owner, decimal Balance, int OperationCount,
    DateTime LastOperationAt, IReadOnlyList<OperationView> LatestOperations);

public class GetStatementHandler(ReadDb db) : IQueryHandler<GetStatement, StatementView?>
{
    public async Task<StatementView?> Handle(GetStatement query)
    {
        var statement = await db.Statements.AsNoTracking().FirstOrDefaultAsync(r => r.Id == query.AccountId);
        if (statement is null)
            return null;

        var latest = await db.Operations.AsNoTracking()
            .Where(o => o.AccountId == query.AccountId)
            .OrderByDescending(o => o.Date)
            .Take(5)
            .Select(o => new OperationView(o.Label, o.Amount, o.BalanceAfter, o.Date))
            .ToListAsync();

        return new StatementView(statement.Id, statement.Owner, statement.Balance, statement.OperationCount,
            statement.LastOperationAt, latest);
    }
}

// ─── Paginated history ───

public record ListOperations(Guid AccountId, int Page, int Size) : IQuery<IReadOnlyList<OperationView>>;

public class ListOperationsHandler(ReadDb db) : IQueryHandler<ListOperations, IReadOnlyList<OperationView>>
{
    public async Task<IReadOnlyList<OperationView>> Handle(ListOperations query) =>
        await db.Operations.AsNoTracking()
            .Where(o => o.AccountId == query.AccountId)
            .OrderByDescending(o => o.Date)
            .Skip((Math.Max(query.Page, 1) - 1) * query.Size)
            .Take(Math.Clamp(query.Size, 1, 100))
            .Select(o => new OperationView(o.Label, o.Amount, o.BalanceAfter, o.Date))
            .ToListAsync();
}

// ─── Dashboard ───

public record GetDashboard : IQuery<DashboardView>;

public record DashboardView(int AccountCount, decimal TotalOutstanding, int TransferCount, decimal TotalTransferredAmount);

public class GetDashboardHandler(ReadDb db) : IQueryHandler<GetDashboard, DashboardView>
{
    public async Task<DashboardView> Handle(GetDashboard query)
    {
        var s = await db.Statistics.AsNoTracking().FirstOrDefaultAsync() ?? new Statistics();
        return new DashboardView(s.AccountCount, s.TotalOutstanding, s.TransferCount, s.TotalTransferredAmount);
    }
}

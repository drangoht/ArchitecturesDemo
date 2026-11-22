using Med.Bank.Data;
using Med.Bank.Pipeline;
using Med.Mediator;
using Microsoft.EntityFrameworkCore;

namespace Med.Bank.Features.Transfers;

public record ListComplianceAlerts : IQuery<IReadOnlyList<string>>;

internal class ListComplianceAlertsHandler(BankDb db) : IRequestHandler<ListComplianceAlerts, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(ListComplianceAlerts request, CancellationToken ct) =>
        await db.ComplianceAlerts.AsNoTracking().Select(a => a.Reason).ToListAsync(ct);
}

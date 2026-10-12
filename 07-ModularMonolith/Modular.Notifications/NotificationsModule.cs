using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modular.Accounts.Contracts;
using Modular.Shared;
using Modular.Transfers.Contracts;

namespace Modular.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>((sp, options) => options.UseInMemoryDatabase(sp.DatabaseName(Name)));

        // Subscriptions to the other modules' events.
        services.AddScoped<IEventHandler<WithdrawalMade>, OnWithdrawalMade>();
        services.AddScoped<IEventHandler<TransferMade>, OnTransferMade>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGet("/notifications/{accountId:guid}", async (Guid accountId, NotificationsDbContext db) =>
            Results.Ok(await db.Notifications
                .Where(n => n.AccountId == accountId)
                .OrderByDescending(n => n.DateUtc)
                .Select(n => new { n.Message, n.DateUtc })
                .ToListAsync()));
}

using Ddd.AccountKeeping.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ddd.Compliance;

/// <summary>
/// Contact point with Account keeping. It TRANSLATES the published language (TransferSent) into the model
/// of Compliance (ObservedTransfer), then applies the vigilance rules.
/// </summary>
public class TransferMonitoring(VigilanceFiles files, IAccountKeepingApi accountKeeping) : IAccountKeepingSubscriber
{
    public async Task OnTransferSent(TransferSent @event)
    {
        var file = files.Get(@event.SourceAccountId, @event.SourceOwner);
        IReadOnlyList<Alert> alerts;
        lock (file)
            alerts = file.Observe(new ObservedTransfer(@event.Date, @event.AmountInEuros, @event.BeneficiaryIban));

        if (alerts.FirstOrDefault(a => a.Level == AlertLevel.Critical) is { } critical)
            await accountKeeping.BlockAccount(@event.SourceAccountId, $"Preventive block (Compliance): {critical.Reason}");
    }
}

public static class ComplianceExtensions
{
    public static IServiceCollection AddCompliance(this IServiceCollection services)
    {
        services.AddSingleton<VigilanceFiles>();
        services.AddScoped<IAccountKeepingSubscriber, TransferMonitoring>();
        return services;
    }

    /// <summary>The API of the Compliance context, with its own vocabulary.</summary>
    public static void MapCompliance(this IEndpointRouteBuilder app)
    {
        var compliance = app.MapGroup("/compliance");

        compliance.MapGet("/alerts", (VigilanceFiles files) =>
            files.All().SelectMany(d => d.Alerts).OrderByDescending(a => a.Date)
                .Select(a => new { a.AccountId, a.Owner, Level = a.Level.ToString(), a.Reason, a.Date }));

        compliance.MapGet("/files/{accountId:guid}", (Guid accountId, VigilanceFiles files) =>
            files.Find(accountId) is { } d
                ? Results.Ok(new { d.AccountId, d.Owner, d.BlockRequested, d.Transfers, AlertCount = d.Alerts.Count })
                : Results.NotFound());
    }
}

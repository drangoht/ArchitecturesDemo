using Components.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Components.Transfers;

// ─── Public surface of the component ───

public interface ITransfersComponent
{
    Task<Result> Transfer(Guid sourceId, Guid targetId, decimal amount);
    Task<IReadOnlyList<TransferDto>> History(Guid accountId);
}

public record TransferDto(Guid SourceId, Guid TargetId, decimal Amount, DateTime DateUtc);

public static class TransfersExtensions
{
    /// <remarks>Requires the Accounts component to be registered too (AddAccountsComponent).</remarks>
    public static IServiceCollection AddTransfersComponent(this IServiceCollection services, string databaseName = "Transfers")
    {
        services.AddDbContext<TransfersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<ITransfersComponent, TransfersComponent>();
        return services;
    }
}

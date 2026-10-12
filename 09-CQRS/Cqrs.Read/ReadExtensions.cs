using Cqrs.Read.Model;
using Cqrs.Read.Projections;
using Cqrs.Read.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Read;

public static class ReadExtensions
{
    public static IServiceCollection AddReadSide(this IServiceCollection services, Func<IServiceProvider, string> databaseName)
    {
        // A database separate from the write side's: it could use another technology (cache, search engine…).
        services.AddDbContext<ReadDb>((sp, options) => options.UseInMemoryDatabase(databaseName(sp)));
        services.AddScoped<Projector>();
        services.AddScoped<QueryBus>();

        services.AddScoped<IQueryHandler<GetStatement, StatementView?>, GetStatementHandler>();
        services.AddScoped<IQueryHandler<ListOperations, IReadOnlyList<OperationView>>, ListOperationsHandler>();
        services.AddScoped<IQueryHandler<GetDashboard, DashboardView>, GetDashboardHandler>();
        return services;
    }
}

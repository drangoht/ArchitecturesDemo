using Cqrs.Api;
using Cqrs.Write;
using Cqrs.Write.Commands;
using Cqrs.Write.Domain;
using Cqrs.Events;
using Cqrs.Read;
using Cqrs.Read.Projections;
using Cqrs.Read.Queries;

var builder = WebApplication.CreateBuilder(args);

string DatabaseName(IServiceProvider sp, string side) =>
    $"{sp.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Bank"}-{side}";

builder.Services.AddWriteSide(sp => DatabaseName(sp, "write"));
builder.Services.AddReadSide(sp => DatabaseName(sp, "read"));
builder.Services.AddHostedService<ProjectionSynchronizer>();

var app = builder.Build();

// Demo data: it too goes through commands.
using (var scope = app.Services.CreateScope())
{
    var bus = scope.ServiceProvider.GetRequiredService<CommandBus>();
    await bus.Send(new OpenAccount(DemoData.Alice, "Alice", 1000m));
    await bus.Send(new OpenAccount(DemoData.Bob, "Bob", 500m));
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (InvalidCommandException e)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { errors = e.Errors });
    }
    catch (BusinessRuleException e)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { errors = new[] { e.Message } });
    }
    catch (AccountNotFoundException e)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { errors = new[] { e.Message } });
    }
});

// ─── Commands (write): 202 Accepted, the read side will be up to date shortly ───

app.MapPost("/accounts", async (OpeningRequest r, CommandBus bus) =>
{
    var id = Guid.NewGuid();
    await bus.Send(new OpenAccount(id, r.Owner, r.InitialDeposit));
    return Results.Accepted($"/accounts/{id}", new { id });
});

app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, WithdrawalRequest r, CommandBus bus) =>
{
    await bus.Send(new WithdrawMoney(id, r.Amount));
    return Results.Accepted($"/accounts/{id}");
});

app.MapPost("/transfers", async (TransferRequest r, CommandBus bus) =>
{
    var transferId = Guid.NewGuid();
    await bus.Send(new MakeTransfer(transferId, r.Source, r.Target, r.Amount));
    return Results.Accepted($"/accounts/{r.Source}", new { transferId });
});

// ─── Queries (read): served by the read models, without going through the domain ───

app.MapGet("/accounts/{id:guid}", async (Guid id, QueryBus bus) =>
    await bus.Ask(new GetStatement(id)) is { } statement ? Results.Ok(statement) : Results.NotFound());

app.MapGet("/accounts/{id:guid}/operations", (Guid id, int? page, int? size, QueryBus bus) =>
    bus.Ask(new ListOperations(id, page ?? 1, size ?? 20)));

app.MapGet("/dashboard", (QueryBus bus) => bus.Ask(new GetDashboard()));

// Observability: by how many events is the read side lagging behind the write side?
app.MapGet("/admin/projections", async (IEventStream stream, Projector projector) =>
{
    var written = await stream.LastSequence();
    var projected = await projector.LastSequence();
    return Results.Ok(new { lastEventWritten = written, lastEventProjected = projected, lag = written - projected });
});

app.Run();

record OpeningRequest(string Owner, decimal InitialDeposit);
record WithdrawalRequest(decimal Amount);
record TransferRequest(Guid Source, Guid Target, decimal Amount);

public partial class Program;

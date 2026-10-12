using Onion.Application;
using Onion.Domain.Model;
using Onion.Domain.Services;
using Onion.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composition root: each layer is wired here.
builder.Services.AddInfrastructure();
builder.Services.AddSingleton<TransferService>();
builder.Services.AddScoped<AccountsAppService>();
builder.Services.AddScoped<TransfersAppService>();

var app = builder.Build();
await app.Services.SeedDemoData();

// Domain errors are translated into HTTP once, by a middleware.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (AccountNotFoundException e)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
    catch (BusinessRuleException e)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

app.MapGet("/accounts/{id:guid}", (Guid id, AccountsAppService accounts) => accounts.Get(id));

app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest request, AccountsAppService accounts) =>
    accounts.Withdraw(id, request.Amount));

app.MapPost("/transfers", (TransferRequest request, TransfersAppService transfers) =>
    transfers.Transfer(request.Source, request.Target, request.Amount));

app.Run();

record WithdrawalRequest(decimal Amount);
record TransferRequest(Guid Source, Guid Target, decimal Amount);

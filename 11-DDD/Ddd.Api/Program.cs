using Ddd.Compliance;
using Ddd.AccountKeeping.Application;
using Ddd.AccountKeeping.Domain;
using Ddd.AccountKeeping.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAccountKeeping(sp => sp.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "AccountKeeping");
builder.Services.AddCompliance();

var app = builder.Build();

// Demo accounts: Alice and Bob in euros, Carla in dollars (to see the conversion).
using (var scope = app.Services.CreateScope())
{
    var accounts = scope.ServiceProvider.GetRequiredService<AccountsService>();
    await accounts.Open("Alice Martin", 50_000m, "EUR", DemoData.Alice);
    await accounts.Open("Bob Durand", 500m, "EUR", DemoData.Bob);
    await accounts.Open("Carla Smith", 1_000m, "USD", DemoData.Carla);
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (BusinessRuleException e)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
    catch (AccountNotFoundException e)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

// ─── "Account keeping" context ───
app.MapGet("/accounts", (AccountsService accounts) => accounts.List());
app.MapGet("/accounts/{id:guid}", (Guid id, AccountsService accounts) => accounts.Get(id));
app.MapPost("/accounts", (OpeningRequest r, AccountsService accounts) => accounts.Open(r.Owner, r.InitialDeposit, r.Currency ?? "EUR"));
app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest r, AccountsService accounts) => accounts.Withdraw(id, r.Amount));
app.MapPost("/accounts/{id:guid}/transfers", (Guid id, TransferRequest r, AccountsService accounts) =>
    accounts.Transfer(id, r.BeneficiaryIban, r.Amount));

// ─── "Compliance" context ───
app.MapCompliance();

app.Run();

record OpeningRequest(string Owner, decimal InitialDeposit, string? Currency);
record WithdrawalRequest(decimal Amount);
record TransferRequest(string BeneficiaryIban, decimal Amount);

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

public partial class Program;

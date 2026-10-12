using Components.Accounts;
using Components.Transfers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAccountsComponent();
builder.Services.AddTransfersComponent();

var app = builder.Build();

// Even the seeding goes through the component's public interface.
using (var scope = app.Services.CreateScope())
{
    var accounts = scope.ServiceProvider.GetRequiredService<IAccountsComponent>();
    await accounts.Open("Alice", 1000m, Guid.Parse("11111111-1111-1111-1111-111111111111"));
    await accounts.Open("Bob", 500m, Guid.Parse("22222222-2222-2222-2222-222222222222"));
}

// Try writing "new AccountsDbContext(...)" or "new Account(...)" here: it does not compile.

app.MapGet("/accounts/{id:guid}", async (Guid id, IAccountsComponent accounts) =>
    await accounts.Get(id) is { } account ? Results.Ok(account) : Results.NotFound());

app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, WithdrawalRequest request, IAccountsComponent accounts) =>
    ToHttp(await accounts.Withdraw(id, request.Amount)));

app.MapPost("/transfers", async (TransferRequest request, ITransfersComponent transfers) =>
    ToHttp(await transfers.Transfer(request.Source, request.Target, request.Amount)));

app.MapGet("/accounts/{id:guid}/transfers", (Guid id, ITransfersComponent transfers) =>
    transfers.History(id));

app.Run();

static IResult ToHttp(Result result) => result.Status switch
{
    ResultStatus.Ok => Results.NoContent(),
    ResultStatus.NotFound => Results.NotFound(new { error = result.Error }),
    _ => Results.BadRequest(new { error = result.Error })
};

record WithdrawalRequest(decimal Amount);
record TransferRequest(Guid Source, Guid Target, decimal Amount);

using System.Globalization;
using Fig.Legacy;
using Microsoft.Extensions.DependencyInjection.Extensions;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BankingSystem>();

var app = builder.Build();

// The business errors, in the historical format: {"error": "INSUFFICIENT BALANCE"}.
app.Use(async (http, next) =>
{
    try
    {
        await next(http);
    }
    catch (BusinessError e)
    {
        http.Response.StatusCode = e.Status;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

// ─── The historical public API: the CONTRACT the clients depend on ───

app.MapGet("/accounts/{number}", (string number, BankingSystem bank) =>
{
    var account = bank.Account(number);
    return Results.Ok(new { number = account.Number, owner = account.Owner, balance = account.BalanceCents / 100m });
});

app.MapGet("/accounts/{number}/operations", (string number, BankingSystem bank) =>
    Results.Ok(bank.Operations(number).Select(o => new
    {
        date = o.Date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        label = o.Label,
        amount = o.AmountCents / 100m
    })));

app.MapGet("/accounts/{number}/statement", (string number, string? month, BankingSystem bank) =>
    Results.Text(bank.Statement(number, month), "text/plain; charset=utf-8"));

app.MapPost("/transfers", (TransferRequest r, BankingSystem bank) =>
{
    var (reference, balance) = bank.Transfer(r.Source, r.Target, r.Amount);
    return Results.Ok(new { reference, sourceBalance = balance / 100m });
});

// A function nobody thought of migrating: it is not in the facade's routing table.
app.MapGet("/branches", () => Results.Ok(new[]
{
    new { code = "075", city = "PARIS OPERA" },
    new { code = "069", city = "LYON BELLECOUR" }
}));

// ─── Added for the migration, reserved for the modern service (the facade never relays it) ───

app.MapGet("/internal/log", (long? since, int? max, BankingSystem bank) =>
    Results.Ok(bank.Log(since ?? 0, Math.Clamp(max ?? 100, 1, 1_000))));

app.MapPost("/internal/imported-operations", (ImportedOperation[] operations, BankingSystem bank) =>
    Results.Ok(new { imported = bank.Import(operations) }));

// The night batch (fees), triggered by hand for the demo.
app.MapPost("/internal/fees", (FeeRequest fee, BankingSystem bank) =>
{
    bank.ChargeFee(fee);
    return Results.NoContent();
});

app.Run();

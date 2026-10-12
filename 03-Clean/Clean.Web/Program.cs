using Clean.InterfaceAdapters.Controllers;
using Clean.InterfaceAdapters.Gateways;
using Clean.InterfaceAdapters.Presenters;
using Clean.UseCases.Lookup;
using Clean.UseCases.Gateways;
using Clean.UseCases.Withdrawal;
using Clean.UseCases.Transfer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IAccountsGateway>(InMemoryAccountsGateway.WithDemoData());
builder.Services.AddScoped<ILookupInput, LookupInteractor>();
builder.Services.AddScoped<IWithdrawalInput, WithdrawalInteractor>();
builder.Services.AddScoped<ITransferInput, TransferInteractor>();
builder.Services.AddScoped<AccountsController>();

var app = builder.Build();

// Each route: create the presenter → call the controller → write the view produced by the presenter.

app.MapGet("/accounts/{id:guid}", async (Guid id, string? format, AccountsController controller) =>
{
    if (format == "text")
    {
        var text = new LookupTextPresenter();
        await controller.Get(id, text);
        return Results.Text(text.Text, "text/plain; charset=utf-8", statusCode: text.Status);
    }

    var presenter = new LookupHttpPresenter();
    await controller.Get(id, presenter);
    return ToHttp(presenter.View);
});

app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, WithdrawalBody body, AccountsController controller) =>
{
    var presenter = new WithdrawalHttpPresenter();
    await controller.Withdraw(id, body.Amount, presenter);
    return ToHttp(presenter.View);
});

app.MapPost("/transfers", async (TransferBody body, AccountsController controller) =>
{
    var presenter = new TransferHttpPresenter();
    await controller.Transfer(body.Source, body.Target, body.Amount, presenter);
    return ToHttp(presenter.View);
});

app.Run();

static IResult ToHttp(HttpView view) => Results.Json(view.Body, statusCode: view.Status);

record WithdrawalBody(decimal Amount);
record TransferBody(Guid Source, Guid Target, decimal Amount);

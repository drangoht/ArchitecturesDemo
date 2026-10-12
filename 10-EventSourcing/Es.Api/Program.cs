using Es.Api;
using Es.Domain;
using Es.Projections;
using Es.Storage;

var builder = WebApplication.CreateBuilder(args);

// Empty Storage:File → in-memory store (used by the tests).
var file = builder.Configuration["Storage:File"];
var filePath = string.IsNullOrWhiteSpace(file) ? null : Path.Combine(builder.Environment.ContentRootPath, file);

builder.Services.AddSingleton<IEventStore>(new EventStore(filePath));
builder.Services.AddSingleton<SnapshotStore>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddSingleton<StatementsProjection>();
builder.Services.AddSingleton<LargestWithdrawalsProjection>();
builder.Services.AddSingleton<ProjectionEngine>();
builder.Services.AddHostedService<ProjectionSubscription>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// First start (empty store): the demo accounts are created… through events, of course.
if ((await app.Services.GetRequiredService<IEventStore>().ReadAll(maximum: 1)).Count == 0)
{
    using var scope = app.Services.CreateScope();
    var now = DateTime.UtcNow;
    await scope.ServiceProvider.GetRequiredService<AccountRepository>().Save(
        Account.Open(DemoData.Alice, "Alice", 1000m, now),
        Account.Open(DemoData.Bob, "Bob", 500m, now));
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
    catch (ConcurrencyConflictException e)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

app.MapCommands();
app.MapReads();

app.Run();

public partial class Program;

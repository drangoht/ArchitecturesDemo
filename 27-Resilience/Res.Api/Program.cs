using Res.Api;
using Res.Bank;
using Res.FraudSimulator;
using Res.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ResilienceOptions());
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton<Ledger>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddSingleton<FraudPartner>();

// The shared state of the strategies: one breaker and one bulkhead for this partner, whatever the request.
builder.Services.AddSingleton<ResilienceEvents>();
builder.Services.AddSingleton(sp => new CircuitBreaker(
    sp.GetRequiredService<ResilienceOptions>().CircuitBreaker, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ResilienceEvents>()));
builder.Services.AddSingleton(sp => new Bulkhead(
    sp.GetRequiredService<ResilienceOptions>().Bulkhead, sp.GetRequiredService<ResilienceEvents>()));

// The pipeline of the fraud client, from the outside in: the order IS the design.
builder.Services.AddHttpClient<IFraudCheck, FraudClient>(client => client.BaseAddress = new Uri("http://fraud.partner.example/"))
    .AddHttpMessageHandler(sp => new BulkheadHandler(sp.GetRequiredService<Bulkhead>()))
    .AddHttpMessageHandler(sp => new RetryHandler(sp.GetRequiredService<ResilienceOptions>().Retry,
        sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ResilienceEvents>(), sp.GetRequiredService<Random>()))
    .AddHttpMessageHandler(sp => new CircuitBreakerHandler(sp.GetRequiredService<CircuitBreaker>()))
    .AddHttpMessageHandler(sp => new TimeoutHandler(sp.GetRequiredService<ResilienceOptions>().AttemptTimeout,
        sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ResilienceEvents>()))
    .ConfigurePrimaryHttpMessageHandler(sp => new SimulatedFraudHandler(sp.GetRequiredService<FraudPartner>()));

var app = builder.Build();

BankEndpoints.Map(app);

app.Run();

// Makes Program visible to WebApplicationFactory in the integration tests.
public partial class Program;

using Micro.Broker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Broker>();
builder.Services.AddHttpClient("subscribers", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHostedService<Dispatcher>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "broker", status = "ok" }));

// Publication: the message is accepted (202) then delivered later, asynchronously.
app.MapPost("/topics/{topic}", async (string topic, HttpRequest request, Broker broker) =>
{
    var messageId = request.Headers["Message-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    var subscribers = broker.Publish(topic, messageId, body);
    return Results.Accepted(value: new { messageId, subscribers });
});

// Observability: what is waiting, what was delivered, what was given up.
app.MapGet("/state", (Broker broker) => broker.State());

app.Run();

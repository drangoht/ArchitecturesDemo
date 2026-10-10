using Micro.Courtier;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Courtier>();
builder.Services.AddHttpClient("abonnes", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHostedService<Distributeur>();

var app = builder.Build();

app.MapGet("/sante", () => Results.Ok(new { service = "courtier", statut = "ok" }));

// Publication : le message est accepté (202) puis livré plus tard, de façon asynchrone.
app.MapPost("/sujets/{sujet}", async (string sujet, HttpRequest requete, Courtier courtier) =>
{
    var messageId = requete.Headers["Message-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    using var lecteur = new StreamReader(requete.Body);
    var corps = await lecteur.ReadToEndAsync();

    var abonnes = courtier.Publier(sujet, messageId, corps);
    return Results.Accepted(value: new { messageId, abonnes });
});

// Observabilité : ce qui attend, ce qui a été livré, ce qui a été abandonné.
app.MapGet("/etat", (Courtier courtier) => courtier.Etat());

app.Run();

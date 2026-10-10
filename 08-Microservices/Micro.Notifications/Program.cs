using Micro.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationsDb>((sp, options) =>
    options.UseInMemoryDatabase(sp.GetRequiredService<IConfiguration>()["NomBase"] ?? "Notifications"));

var app = builder.Build();

app.MapGet("/sante", () => Results.Ok(new { service = "notifications", statut = "ok" }));

// Points de réception des messages (appelés par le courtier, non exposés par la passerelle).
app.MapPost("/evenements/retrait-effectue",
    ([FromHeader(Name = "Message-Id")] Guid messageId, RetraitEffectue e, NotificationsDb db) =>
        Traiter(db, messageId,
            new Notification(e.CompteId, $"Retrait de {e.Montant:0.00} €. Nouveau solde : {e.NouveauSolde:0.00} €.")));

app.MapPost("/evenements/virement-effectue",
    ([FromHeader(Name = "Message-Id")] Guid messageId, VirementEffectue e, NotificationsDb db) =>
        Traiter(db, messageId,
            new Notification(e.SourceId, $"Virement de {e.Montant:0.00} € envoyé à {e.TitulaireCible}."),
            new Notification(e.CibleId, $"Virement de {e.Montant:0.00} € reçu de {e.TitulaireSource}.")));

// API publique.
app.MapGet("/notifications/{compteId:guid}", async (Guid compteId, NotificationsDb db) =>
    Results.Ok(await db.Notifications
        .Where(n => n.CompteId == compteId)
        .OrderByDescending(n => n.DateUtc)
        .Select(n => new { n.Message, n.DateUtc })
        .ToListAsync()));

app.Run();

// Le courtier livre « au moins une fois » : un même message peut arriver deux fois. On l'ignore alors.
static async Task<IResult> Traiter(NotificationsDb db, Guid messageId, params Notification[] notifications)
{
    if (await db.MessagesTraites.FindAsync(messageId) is not null)
        return Results.Ok(new { doublon = true });

    db.MessagesTraites.Add(new MessageTraite { MessageId = messageId });
    db.Notifications.AddRange(notifications);
    await db.SaveChangesAsync();
    return Results.Ok(new { doublon = false });
}

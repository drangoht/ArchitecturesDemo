using Micro.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NotificationsDb>((sp, options) =>
    options.UseInMemoryDatabase(sp.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Notifications"));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "notifications", status = "ok" }));

// Message reception points (called by the broker, not exposed by the gateway).
app.MapPost("/events/withdrawal-made",
    ([FromHeader(Name = "Message-Id")] Guid messageId, WithdrawalMade e, NotificationsDb db) =>
        Handle(db, messageId,
            new Notification(e.AccountId, $"Withdrawal of {e.Amount:0.00} €. New balance: {e.NewBalance:0.00} €.")));

app.MapPost("/events/transfer-made",
    ([FromHeader(Name = "Message-Id")] Guid messageId, TransferMade e, NotificationsDb db) =>
        Handle(db, messageId,
            new Notification(e.SourceId, $"Transfer of {e.Amount:0.00} € sent to {e.TargetOwner}."),
            new Notification(e.TargetId, $"Transfer of {e.Amount:0.00} € received from {e.SourceOwner}.")));

// Public API.
app.MapGet("/notifications/{accountId:guid}", async (Guid accountId, NotificationsDb db) =>
    Results.Ok(await db.Notifications
        .Where(n => n.AccountId == accountId)
        .OrderByDescending(n => n.DateUtc)
        .Select(n => new { n.Message, n.DateUtc })
        .ToListAsync()));

app.Run();

// The broker delivers "at least once": the same message can arrive twice. It is then ignored.
static async Task<IResult> Handle(NotificationsDb db, Guid messageId, params Notification[] notifications)
{
    if (await db.ProcessedMessages.FindAsync(messageId) is not null)
        return Results.Ok(new { duplicate = true });

    db.ProcessedMessages.Add(new ProcessedMessage { MessageId = messageId });
    db.Notifications.AddRange(notifications);
    await db.SaveChangesAsync();
    return Results.Ok(new { duplicate = false });
}

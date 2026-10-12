using Sls.Platform;

namespace Sls.Functions;

/// <summary>
/// The deployment manifest (the equivalent of serverless.yml): which functions, triggered by what,
/// with how much memory. There is no server, no controller, no Program.cs to write.
/// </summary>
public static class Deployment
{
    public static void Declare(Manifest manifest) => manifest
        // HTTP triggers (behind a managed API gateway)
        .Http("OpenAccount", "POST", "/accounts", AccountsFunctions.Open)
        .Http("GetAccount", "GET", "/accounts/{id}", AccountsFunctions.Get)
        .Http("Withdraw", "POST", "/accounts/{id}/withdrawal", AccountsFunctions.Withdraw)
        .Http("RequestTransfer", "POST", "/transfers", TransfersFunctions.Request)
        .Http("TrackTransfer", "GET", "/transfers/{id}", TransfersFunctions.Track)
        .Http("ListNotifications", "GET", "/accounts/{id}/notifications", NotificationsFunctions.List)
        .Http("UploadImport", "POST", "/imports", ImportsFunctions.Upload)
        .Http("CountVisits", "GET", "/visits", VisitsFunctions.Count)

        // Queue triggers: asynchronous processing, with automatic retries
        .Queue("ExecuteTransfer", Queues.Transfers, TransfersFunctions.Execute, memoryMb: 256)
        .Queue("SendNotification", Queues.Notifications, NotificationsFunctions.Send)

        // Storage trigger: an uploaded file starts its processing
        .Storage("ProcessImport", "imports/", ImportsFunctions.Process, memoryMb: 512)

        // Scheduled trigger
        .Timer("ProduceReport", TimeSpan.FromSeconds(30), ReportsFunctions.Produce);
}

public static class Tables
{
    public const string Accounts = "accounts";
    public const string Transfers = "transfers";
    public const string Notifications = "notifications";
    public const string Counters = "counters";
}

public static class Queues
{
    public const string Transfers = "transfers";
    public const string Notifications = "notifications";
}

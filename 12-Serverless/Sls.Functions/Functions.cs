using System.Globalization;
using System.Text.Json;
using Sls.Platform;

namespace Sls.Functions;

// Each function is a static method: input = the trigger + the context, output = a response (or nothing).
// No state in memory between two calls: everything goes through the managed services (tables, queues, storage).

public static class AccountsFunctions
{
    public record OpeningRequest(string Owner, decimal InitialDeposit);
    public record WithdrawalRequest(decimal Amount);

    public static async Task<HttpResponseData> Open(HttpRequestData request, FunctionContext context)
    {
        var demand = request.Read<OpeningRequest>();
        if (string.IsNullOrWhiteSpace(demand.Owner) || demand.InitialDeposit < 10)
            return HttpResponseData.Invalid("Owner required and initial deposit of at least 10 €.");

        var account = new Account(Guid.NewGuid(), demand.Owner.Trim(), demand.InitialDeposit, []);
        await context.Table(Tables.Accounts).Write(account.Id.ToString(), account, expectedEtag: 0);
        context.Log($"account opened for {account.Owner}");
        return HttpResponseData.Created($"/accounts/{account.Id}", account);
    }

    public static async Task<HttpResponseData> Get(HttpRequestData request, FunctionContext context)
    {
        var (account, _) = await context.Table(Tables.Accounts).Read<Account>(request.Parameters["id"]);
        return account is null ? HttpResponseData.NotFound("Account not found.") : HttpResponseData.Ok(account);
    }

    public static async Task<HttpResponseData> Withdraw(HttpRequestData request, FunctionContext context)
    {
        var amount = request.Read<WithdrawalRequest>().Amount;
        if (amount <= 0)
            return HttpResponseData.Invalid("The amount must be strictly positive.");
        try
        {
            var account = await Concurrency.Modify<Account>(context.Table(Tables.Accounts), request.Parameters["id"],
                c => c.Apply(Guid.NewGuid(), -amount));
            return HttpResponseData.Ok(account);
        }
        catch (KeyNotFoundException)
        {
            return HttpResponseData.NotFound("Account not found.");
        }
        catch (BusinessRuleException e)
        {
            return HttpResponseData.Invalid(e.Message);
        }
    }
}

public static class TransfersFunctions
{
    public record TransferRequest(Guid Source, Guid Target, decimal Amount);
    public record TransferMessage(Guid TransferId);

    /// <summary>HTTP: the transfer is NOT made here. It is accepted (202) and handed to a queue: immediate response.</summary>
    public static async Task<HttpResponseData> Request(HttpRequestData request, FunctionContext context)
    {
        var demand = request.Read<TransferRequest>();
        if (await Validate(demand, context) is { } error)
            return HttpResponseData.Invalid(error);

        var transfer = await Schedule(demand, context, Guid.NewGuid());
        return HttpResponseData.Accepted($"/transfers/{transfer.Id}", new { transferId = transfer.Id, status = transfer.Status });
    }

    public static async Task<HttpResponseData> Track(HttpRequestData request, FunctionContext context)
    {
        var (transfer, _) = await context.Table(Tables.Transfers).Read<Transfer>(request.Parameters["id"]);
        return transfer is null ? HttpResponseData.NotFound("Transfer not found.") : HttpResponseData.Ok(transfer);
    }

    internal static async Task<string?> Validate(TransferRequest demand, FunctionContext context)
    {
        if (demand.Amount <= 0)
            return "The amount must be strictly positive.";
        if (demand.Source == demand.Target)
            return "Cannot transfer to the same account.";
        var accounts = context.Table(Tables.Accounts);
        if ((await accounts.Read<Account>(demand.Source.ToString())).Value is null
            || (await accounts.Read<Account>(demand.Target.ToString())).Value is null)
            return "Source or target account not found.";
        return null;
    }

    /// <summary>Saves the transfer as "pending" and sends it to the queue. Replayable: an already known identifier is not created again.</summary>
    internal static async Task<Transfer> Schedule(TransferRequest demand, FunctionContext context, Guid id)
    {
        var transfers = context.Table(Tables.Transfers);
        var transfer = new Transfer(id, demand.Source, demand.Target, demand.Amount, "Pending");
        try
        {
            await transfers.Write(id.ToString(), transfer, expectedEtag: 0);
        }
        catch (WriteConflictException)
        {
            transfer = (await transfers.Read<Transfer>(id.ToString())).Value!;
            if (transfer.Status != "Pending")
                return transfer;  // already processed: nothing to redo
        }
        // When in doubt, the message is sent again: its execution is idempotent.
        await context.SendToQueue(Queues.Transfers, new TransferMessage(id));
        return transfer;
    }

    /// <summary>
    /// "transfers" queue. Can run several times for the same message (retry, double delivery):
    /// each step is idempotent (the transfer identifier serves as the operation identifier on the accounts).
    /// An exception = automatic retry; a business refusal, however, is final.
    /// </summary>
    public static async Task Execute(QueueMessage message, FunctionContext context)
    {
        var id = message.Read<TransferMessage>().TransferId;
        var (transfers, accounts) = (context.Table(Tables.Transfers), context.Table(Tables.Accounts));

        var (transfer, _) = await transfers.Read<Transfer>(id.ToString());
        if (transfer is null || transfer.Status != "Pending")
            return;

        Account source;
        try
        {
            source = await Concurrency.Modify<Account>(accounts, transfer.Source.ToString(), c => c.Apply(id, -transfer.Amount));
        }
        catch (BusinessRuleException e)
        {
            await transfers.Write(id.ToString(), transfer with { Status = "Refused", Error = e.Message });
            context.Log($"transfer {id} refused: {e.Message}");
            return;
        }

        await Task.Delay(200, context.Cancellation);  // simulation: call to the interbank clearing system

        var target = await Concurrency.Modify<Account>(accounts, transfer.Target.ToString(), c => c.Apply(id, transfer.Amount));

        var amount = transfer.Amount.ToString("N2", CultureInfo.GetCultureInfo("en-GB"));
        await context.SendToQueue(Queues.Notifications,
            new NotificationsFunctions.NotificationMessage(source.Id, $"transfer-{id}", $"Transfer of {amount} € sent to {target.Owner}."));
        await context.SendToQueue(Queues.Notifications,
            new NotificationsFunctions.NotificationMessage(target.Id, $"transfer-{id}", $"Transfer of {amount} € received from {source.Owner}."));

        await transfers.Write(id.ToString(), transfer with { Status = "Done" });
        context.Log($"transfer {id} done ({amount} €)");
    }
}

public static class NotificationsFunctions
{
    public record NotificationMessage(Guid AccountId, string Key, string Text);

    /// <summary>"notifications" queue. If the text message provider is down, the exception triggers retries.</summary>
    public static async Task Send(QueueMessage message, FunctionContext context)
    {
        if (context.Setting("Sms:Down") == "true")
            throw new InvalidOperationException("The text message provider does not respond.");

        var notification = message.Read<NotificationMessage>();
        // Deterministic key: the same notification replayed overwrites the previous one instead of creating a duplicate.
        await context.Table(Tables.Notifications).Write($"{notification.AccountId}/{notification.Key}",
            new Notification(notification.AccountId, notification.Text, DateTime.UtcNow));
        context.Log($"text message sent: {notification.Text}");
    }

    public static async Task<HttpResponseData> List(HttpRequestData request, FunctionContext context) =>
        HttpResponseData.Ok(await context.Table(Tables.Notifications).List<Notification>($"{request.Parameters["id"]}/"));
}

public static class ImportsFunctions
{
    /// <summary>HTTP: uploads the CSV file to the storage. It is the arrival of the file that triggers its processing.</summary>
    public static async Task<HttpResponseData> Upload(HttpRequestData request, FunctionContext context)
    {
        var path = $"imports/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv";
        await context.WriteObject(path, request.Body);
        return HttpResponseData.Accepted($"/platform/storage/content?path={path}", new { file = path });
    }

    /// <summary>
    /// "imports/" storage: one line = one message in the transfers queue ("fan-out").
    /// The platform then starts as many ExecuteTransfer instances as needed, in parallel.
    /// </summary>
    public static async Task Process(StoredObject file, FunctionContext context)
    {
        var lines = file.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (scheduled, rejected) = (0, 0);
        for (var number = 0; number < lines.Length; number++)
        {
            var fields = lines[number].Split(';');
            if (fields.Length != 3 || !Guid.TryParse(fields[0], out var source) || !Guid.TryParse(fields[1], out var target)
                || !decimal.TryParse(fields[2], CultureInfo.InvariantCulture, out var amount))
                continue;  // header or unreadable line

            var demand = new TransfersFunctions.TransferRequest(source, target, amount);
            if (await TransfersFunctions.Validate(demand, context) is { } error)
            {
                context.Log($"line {number + 1} rejected: {error}");
                rejected++;
                continue;
            }
            // Identifier derived from the file and the line number: reprocessing the file creates no duplicate.
            await TransfersFunctions.Schedule(demand, context, Concurrency.DeterministicId($"{file.Path}#{number}"));
            scheduled++;
        }
        context.Log($"{file.Path}: {scheduled} transfers scheduled, {rejected} lines rejected");
    }
}

public static class ReportsFunctions
{
    /// <summary>Timer: produces a report in the storage, with no server running between two executions.</summary>
    public static async Task Produce(TimerTick tick, FunctionContext context)
    {
        var accounts = await context.Table(Tables.Accounts).List<Account>();
        var transfers = await context.Table(Tables.Transfers).List<Transfer>();
        var report = new
        {
            time = tick.Time,
            accountCount = accounts.Count,
            totalOutstanding = accounts.Sum(c => c.Balance),
            transfers = transfers.GroupBy(v => v.Status).ToDictionary(g => g.Key, g => g.Count())
        };
        await context.WriteObject($"reports/{tick.Time:yyyyMMdd-HHmmss}.json", JsonSerializer.Serialize(report));
    }
}

public static class VisitsFunctions
{
    /// <summary>Shows the difference between an instance's memory (misleading) and a managed service (reliable).</summary>
    public static async Task<HttpResponseData> Count(HttpRequestData request, FunctionContext context)
    {
        // ✗ Instance memory: specific to THIS instance, reset at each cold start.
        var instanceCounter = (context.InstanceMemory.TryGetValue("visits", out var v) ? (long)v : 0) + 1;
        context.InstanceMemory["visits"] = instanceCounter;

        // ✓ Managed service: shared by all the instances, durable.
        var global = await Concurrency.ModifyOrCreate(context.Table(Tables.Counters), "visits",
            new Counter(0), c => c with { Value = c.Value + 1 });

        return HttpResponseData.Ok(new
        {
            instance = context.InstanceId,
            coldStart = context.ColdStart,
            instanceCounter,
            globalCounter = global.Value
        });
    }
}

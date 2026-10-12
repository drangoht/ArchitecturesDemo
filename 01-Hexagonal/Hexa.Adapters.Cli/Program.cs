using System.Globalization;
using System.Text;
using Hexa.Adapters.Notifications;
using Hexa.Adapters.Persistence;
using Hexa.Hexagon.Application;
using Hexa.Hexagon.Domain;
using Hexa.Hexagon.Ports.Inbound;
using Hexa.Hexagon.Ports.Outbound;

// "Command line" inbound adapter: same hexagon as the web API, another entry door.
// Manual composition root, without a dependency injection container: the hexagon does not need one.

Console.OutputEncoding = Encoding.UTF8;

IAccounts accounts = new JsonFileAccounts("accounts.json", DemoData.Accounts());
INotifications notifications = new LogNotifications(Console.Out);

IGetAccount get = new GetAccount(accounts);
IWithdrawMoney withdraw = new WithdrawMoney(accounts, notifications);
IMakeTransfer transfer = new MakeTransfer(accounts, notifications);

try
{
    switch (args)
    {
        case ["balance", var id]:
            var view = await get.Execute(Guid.Parse(id));
            Console.WriteLine($"{view.Owner}: {view.Balance:0.00} €");
            break;

        case ["withdraw", var id, var amount]:
            await withdraw.Execute(Guid.Parse(id), Amount(amount));
            Console.WriteLine("Withdrawal done.");
            break;

        case ["transfer", var source, var target, var amount]:
            await transfer.Execute(Guid.Parse(source), Guid.Parse(target), Amount(amount));
            Console.WriteLine("Transfer done.");
            break;

        default:
            Console.WriteLine("""
                Usage:
                  balance  <accountId>
                  withdraw <accountId> <amount>
                  transfer <sourceId> <targetId> <amount>

                Demo accounts (stored in accounts.json, in the current folder):
                  Alice = 11111111-1111-1111-1111-111111111111
                  Bob   = 22222222-2222-2222-2222-222222222222
                """);
            return 1;
    }
    return 0;
}
catch (Exception e) when (e is BusinessRuleException or AccountNotFoundException)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 2;
}

static decimal Amount(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

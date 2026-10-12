using Ui.Bank;
using Ui.Mvc.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ui.Mvc.Controllers;

/// <summary>
/// An MVC controller: one action per page. Each one reads the model, prepares the view's data, then designates the view
/// (by convention: Views/Accounts/{action}.cshtml). The controller produces no HTML and the view does not read the bank.
/// </summary>
public sealed class AccountsController(IBank bank) : Controller
{
    // GET /  or  /Accounts
    public async Task<IActionResult> Index(CancellationToken cancellation)
    {
        var accounts = await bank.AccountsAsync(cancellation);
        return View(new AccountList(accounts.Select(Line).ToList()));
    }

    // GET /Accounts/Detail/{id}
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellation)
    {
        try
        {
            var account = await bank.AccountAsync(id, cancellation);
            var operations = await bank.OperationsAsync(id, cancellation);
            return View(new AccountDetail(Line(account), operations.Select(OperationLine).ToList()));
        }
        catch (BusinessError)
        {
            return NotFound();
        }
    }

    internal static AccountLine Line(Account c) => new(c.Id, c.Owner, c.Label, Format.Euros(c.Balance));

    private static OperationLine OperationLine(Operation o) =>
        new(Format.Date(o.Date), o.Label,
            (o.Amount > 0 ? "+" : "") + Format.Euros(o.Amount), o.Amount < 0);
}

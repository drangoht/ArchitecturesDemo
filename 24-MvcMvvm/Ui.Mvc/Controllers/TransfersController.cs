using Ui.Bank;
using Ui.Mvc.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ui.Mvc.Controllers;

/// <summary>
/// The transfer form, in the three typical stages of web MVC:
/// GET displays the form; POST validates it and, if it is wrong, DISPLAYS AGAIN the same view with the errors (200);
/// if it is right, executes then REDIRECTS (302, "Post/Redirect/Get"): refreshing the landing page does not redo the transfer.
/// </summary>
public sealed class TransfersController(IBank bank) : Controller
{
    // GET /Transfers/New?source={id}
    [HttpGet]
    public async Task<IActionResult> New(Guid? source, CancellationToken cancellation)
    {
        var form = new TransferForm { Source = source };
        await LoadAccounts(form, cancellation);
        return View(form);
    }

    // POST /Transfers/New  (form fields + anti-forgery token, checked by the global filter)
    [HttpPost]
    public async Task<IActionResult> New(TransferForm form, CancellationToken cancellation)
    {
        // Model binding has already filled the form and noted in ModelState what is wrong:
        // missing field, "abc" instead of a number, rules of the attributes and of Validate.
        if (!ModelState.IsValid)
            return await Redisplay(form, cancellation);

        var (source, target, amount) = (form.Source!.Value, form.Target!.Value, form.Amount!.Value);

        // One more step for large amounts: another view, which sends back the same fields (hidden) and Confirmed=true.
        if (amount >= TransferRules.ConfirmationThreshold && !form.Confirmed)
        {
            await LoadAccounts(form, cancellation);
            return View("Confirm", form);
        }

        try
        {
            var transfer = await bank.TransferAsync(source, target, amount, cancellation);
            // TempData survives ONE redirection (cookie), then disappears: the message is displayed only once.
            TempData["Message"] = $"Transfer of {Format.Euros(transfer.Amount)} done. New balance: {Format.Euros(transfer.SourceBalance)}.";
            return RedirectToAction(nameof(AccountsController.Detail), "Accounts", new { id = source });
        }
        catch (BusinessError error)
        {
            // Refusal of the bank (insufficient balance…): an error without a field, displayed at the top of the form.
            ModelState.AddModelError(string.Empty, error.Message);
            form.Confirmed = false;
            return await Redisplay(form, cancellation);
        }
    }

    private async Task<IActionResult> Redisplay(TransferForm form, CancellationToken cancellation)
    {
        await LoadAccounts(form, cancellation);
        return View(nameof(New), form);
    }

    private async Task LoadAccounts(TransferForm form, CancellationToken cancellation) =>
        form.Accounts = (await bank.AccountsAsync(cancellation)).Select(AccountsController.Line).ToList();
}

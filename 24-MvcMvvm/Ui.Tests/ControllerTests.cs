using Ui.Bank;
using Ui.Mvc.Controllers;
using Ui.Mvc.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Ui.Tests;

/// <summary>
/// The MVC controllers called as plain methods: we check the DECISION (which view, which data, which
/// redirection), not the HTML. What happens before the action (binding, validation) does not take place here: ModelState is
/// filled by hand, and that is the whole point of the SiteTests, which go through the complete pipeline.
/// </summary>
public class ControllerTests
{
    private readonly InMemoryBank _bank = new(new FixedClock());

    private TransfersController Transfers() => new(_bank)
    {
        TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataWithoutStorage()),
    };

    [Fact]
    public async Task The_list_chooses_its_view_and_passes_it_data_ready_to_display()
    {
        var result = Assert.IsType<ViewResult>(await new AccountsController(_bank).Index(default));

        Assert.Null(result.ViewName);   // the view by convention: Views/Accounts/Index.cshtml
        var model = Assert.IsType<AccountList>(result.Model);
        Assert.Equal(["1,000.00 €", "8,000.00 €", "500.00 €"], model.Accounts.Select(c => c.Balance));
    }

    [Fact]
    public async Task An_unknown_account_gives_404()
    {
        Assert.IsType<NotFoundResult>(await new AccountsController(_bank).Detail(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task An_invalid_form_is_displayed_again_without_transferring_anything()
    {
        var controller = Transfers();
        // A complete form, but where the pipeline noted an error: the action must stick to ModelState.
        controller.ModelState.AddModelError("Amount", "The amount is given to the cent.");
        var form = new TransferForm { Source = InMemoryBank.Alice, Target = InMemoryBank.Bob, Amount = 250m };

        var result = Assert.IsType<ViewResult>(await controller.New(form, default));

        Assert.Equal("New", result.ViewName);
        Assert.Equal(3, Assert.IsType<TransferForm>(result.Model).Accounts.Count);   // lists reloaded
        Assert.Equal(1_000m, (await _bank.AccountAsync(InMemoryBank.Alice)).Balance);
    }

    [Fact]
    public async Task A_successful_transfer_redirects_to_the_account_with_a_message()
    {
        var controller = Transfers();
        var form = new TransferForm { Source = InMemoryBank.Alice, Target = InMemoryBank.Bob, Amount = 250.50m };

        var result = Assert.IsType<RedirectToActionResult>(await controller.New(form, default));

        Assert.Equal(("Accounts", "Detail"), (result.ControllerName, result.ActionName));
        Assert.Equal(InMemoryBank.Alice, result.RouteValues!["id"]);
        Assert.Equal("Transfer of 250.50 € done. New balance: 749.50 €.", controller.TempData["Message"]);
    }

    [Fact]
    public async Task A_large_amount_goes_through_the_confirmation_view()
    {
        var form = new TransferForm { Source = InMemoryBank.AliceSavings, Target = InMemoryBank.Bob, Amount = 1_500m };

        var result = Assert.IsType<ViewResult>(await Transfers().New(form, default));

        Assert.Equal("Confirm", result.ViewName);
        Assert.Equal(8_000m, (await _bank.AccountAsync(InMemoryBank.AliceSavings)).Balance);
    }

    [Fact]
    public async Task A_refusal_of_the_bank_becomes_a_form_error()
    {
        var controller = Transfers();
        var form = new TransferForm { Source = InMemoryBank.Bob, Target = InMemoryBank.Alice, Amount = 600m };

        var result = Assert.IsType<ViewResult>(await controller.New(form, default));

        Assert.Equal("New", result.ViewName);
        Assert.Equal("Insufficient balance: 500.00 € available.", Assert.Single(controller.ModelState[string.Empty]!.Errors).ErrorMessage);
    }

    private sealed class TempDataWithoutStorage : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}

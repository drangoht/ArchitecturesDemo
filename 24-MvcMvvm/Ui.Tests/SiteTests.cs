using System.Net;
using System.Text.RegularExpressions;
using Ui.Bank;
using Ui.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ui.Tests;

/// <summary>
/// The complete MVC site, in memory: routing, model binding, validation, anti-forgery token, Razor views,
/// redirection and TempData. We play the role of the browser: we read HTML and send back form fields.
/// </summary>
public class SiteTests : IDisposable
{
    private readonly WebApplicationFactory<Marker> _site = new();
    private readonly HttpClient _browser;

    public SiteTests() =>
        // The cookies are kept (anti-forgery token, TempData); the redirections are followed by hand.
        _browser = _site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void Dispose() => _site.Dispose();

    private IBank Bank => _site.Services.GetRequiredService<IBank>();

    private async Task<string> Page(string url)
    {
        var response = await _browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>What a browser does: read the form (and its hidden token), then send it.</summary>
    private async Task<HttpResponseMessage> Send(Dictionary<string, string> fields, bool withToken = true)
    {
        var form = await Page("/Transfers/New");
        if (withToken)
            fields["__RequestVerificationToken"] = Regex.Match(form, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        return await _browser.PostAsync("/Transfers/New", new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> Fields(Guid source, Guid target, string amount) => new()
    {
        ["Source"] = source.ToString(), ["Target"] = target.ToString(), ["Amount"] = amount,
    };

    [Fact]
    public async Task The_home_page_lists_the_accounts()
    {
        var html = await Page("/");

        Assert.Contains("<td class=\"amount\">8,000.00 €</td>", html);
        Assert.Contains($"href=\"/Accounts/Detail/{InMemoryBank.Bob}\"", html);   // link produced by the asp-action tag helper
    }

    [Fact]
    public async Task An_unknown_account_gives_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync($"/Accounts/Detail/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task The_form_preselects_the_source_account()
    {
        var html = await Page($"/Transfers/New?source={InMemoryBank.Bob}");

        Assert.Contains($"<option selected=\"selected\" value=\"{InMemoryBank.Bob}\">Bob — Current account (500.00 €)</option>", html);
    }

    [Fact]
    public async Task Without_the_anti_forgery_token_the_form_is_refused()
    {
        var response = await Send(Fields(InMemoryBank.Alice, InMemoryBank.Bob, "10"), withToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1_000m, (await Bank.AccountAsync(InMemoryBank.Alice)).Balance);
    }

    [Fact]
    public async Task Post_Redirect_Get_the_message_is_displayed_only_once()
    {
        var response = await Send(Fields(InMemoryBank.Alice, InMemoryBank.Bob, "250.50"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var landing = response.Headers.Location!.ToString();
        Assert.Equal($"/Accounts/Detail/{InMemoryBank.Alice}", landing);

        var html = await Page(landing);
        Assert.Contains("Transfer of 250.50 € done. New balance: 749.50 €.", html);
        Assert.Contains("<strong id=\"balance\">749.50 €</strong>", html);

        // Refreshing the page (F5) reads the landing page again: no second transfer, and the message was consumed.
        var html2 = await Page(landing);
        Assert.DoesNotContain("done", html2);
        Assert.Equal(749.50m, (await Bank.AccountAsync(InMemoryBank.Alice)).Balance);
    }

    [Fact]
    public async Task An_invalid_input_displays_the_form_again_with_what_was_typed()
    {
        var response = await Send(Fields(InMemoryBank.Alice, InMemoryBank.Bob, "abc"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // no redirection: the same view, with the errors
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"abc\" is not a valid amount.", WebUtility.HtmlDecode(html));   // the quotes are encoded
        Assert.Matches("<input[^>]*class=\"input-validation-error\"[^>]*value=\"abc\"", html);
        Assert.Contains($"<option selected=\"selected\" value=\"{InMemoryBank.Bob}\">", html);   // the choices are kept
    }

    [Fact]
    public async Task The_missing_fields_and_the_rules_between_fields_are_checked()
    {
        var empty = await (await Send([])).Content.ReadAsStringAsync();
        Assert.Contains("Choose the account to debit.", empty);
        Assert.Contains("Enter an amount.", empty);

        var sameAccount = await (await Send(Fields(InMemoryBank.Alice, InMemoryBank.Alice, "10"))).Content.ReadAsStringAsync();
        // Reported on the Target field by IValidatableObject, before calling the bank (which would refuse too, but at the top of the page).
        Assert.Matches("data-valmsg-for=\"Target\"[^>]*>Choose two different accounts.</span>", sameAccount);
    }

    [Fact]
    public async Task A_refusal_of_the_bank_is_displayed_at_the_top_of_the_form()
    {
        var response = await Send(Fields(InMemoryBank.Bob, InMemoryBank.Alice, "600"));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("validation-summary-errors[\\s\\S]*Insufficient balance: 500.00 € available.", html);
    }

    [Fact]
    public async Task A_large_amount_asks_for_a_confirmation_page_that_sends_everything_back()
    {
        var step1 = await Send(Fields(InMemoryBank.AliceSavings, InMemoryBank.Bob, "1500"));
        var confirmation = await step1.Content.ReadAsStringAsync();
        Assert.Contains("<h2>Confirm the transfer</h2>", confirmation);
        Assert.Equal(8_000m, (await Bank.AccountAsync(InMemoryBank.AliceSavings)).Balance);

        // The server kept nothing: the confirmation page sends back the hidden fields, plus Confirmed=true.
        var fields = Regex.Matches(confirmation, "<input type=\"hidden\"[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"|<input[^>]*name=\"([^\"]+)\" type=\"hidden\" value=\"([^\"]*)\"")
            .ToDictionary(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value,
                          m => m.Groups[1].Success ? m.Groups[2].Value : m.Groups[4].Value);
        Assert.Equal(["Source", "Target", "Amount", "Confirmed", "__RequestVerificationToken"], fields.Keys);
        var step2 = await _browser.PostAsync("/Transfers/New", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, step2.StatusCode);
        Assert.Equal(6_500m, (await Bank.AccountAsync(InMemoryBank.AliceSavings)).Balance);
    }
}

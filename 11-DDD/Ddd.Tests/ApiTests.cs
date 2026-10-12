using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Ddd.Compliance;
using Ddd.AccountKeeping.Contracts;
using Ddd.AccountKeeping.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ddd.Tests;

public class BankApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("DatabaseName", Guid.NewGuid().ToString());
}

public class ApiTests
{
    private static async Task<JsonElement> Account(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/accounts/{id}");

    private static async Task<string> Iban(HttpClient client, Guid id) => (await Account(client, id)).GetProperty("iban").GetString()!;

    private static Task<HttpResponseMessage> Transfer(HttpClient client, Guid source, string iban, decimal amount) =>
        client.PostAsJsonAsync($"/accounts/{source}/transfers", new { beneficiaryIban = iban, amount });

    [Fact]
    public async Task Three_close_transfers_trigger_a_block_by_compliance()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        var ibanAlice = await Iban(client, DemoData.Alice);

        for (var i = 0; i < 3; i++)
            (await Transfer(client, DemoData.Bob, ibanAlice, 10m)).EnsureSuccessStatusCode();
        var fourth = await Transfer(client, DemoData.Bob, ibanAlice, 10m);

        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
        var bob = await Account(client, DemoData.Bob);
        Assert.Equal("Blocked", bob.GetProperty("status").GetString());
        Assert.Contains("structuring", bob.GetProperty("blockReason").GetString());
        var alerts = await client.GetFromJsonAsync<JsonElement>("/compliance/alerts");
        Assert.Equal("Critical", Assert.Single(alerts.EnumerateArray()).GetProperty("level").GetString());
    }

    [Fact]
    public async Task A_transfer_in_euros_to_an_account_in_dollars_is_converted()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        (await Transfer(client, DemoData.Alice, await Iban(client, DemoData.Carla), 100m)).EnsureSuccessStatusCode();

        var carla = await Account(client, DemoData.Carla);
        Assert.Equal(1_108.32m, carla.GetProperty("balance").GetDecimal());
        Assert.Equal("USD", carla.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task A_large_transfer_is_reported_without_blocking_the_account()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        (await Transfer(client, DemoData.Alice, await Iban(client, DemoData.Bob), 12_000m)).EnsureSuccessStatusCode();

        var alerts = await client.GetFromJsonAsync<JsonElement>("/compliance/alerts");
        Assert.Equal("High", Assert.Single(alerts.EnumerateArray()).GetProperty("level").GetString());
        Assert.Equal("Active", (await Account(client, DemoData.Alice)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_invalid_iban_is_refused_with_a_business_message()
    {
        await using var app = new BankApplication();

        var response = await Transfer(app.CreateClient(), DemoData.Alice, "FR00 1234", 10m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Invalid IBAN", await response.Content.ReadAsStringAsync());
    }

    // ─── Boundaries of the bounded contexts ───

    private static string[] InternalDependencies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Ddd.")).ToArray();

    [Fact]
    public void The_domain_depends_on_nothing()
    {
        Assert.Empty(InternalDependencies(typeof(BusinessRuleException).Assembly));
    }

    [Fact]
    public void Compliance_only_knows_the_published_language_of_account_keeping()
    {
        Assert.Equal(["Ddd.AccountKeeping.Contracts"], InternalDependencies(typeof(VigilanceFile).Assembly));
    }

    [Fact]
    public void The_published_language_exposes_no_domain_type()
    {
        Assert.Empty(InternalDependencies(typeof(TransferSent).Assembly));
    }
}

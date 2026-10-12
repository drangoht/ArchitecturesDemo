using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mk.Host;

namespace Mk.Tests;

public class BankWithPlugins(DateTime localTime) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Plugins:Folder", LoadingTests.PluginsFolder());
        builder.UseSetting("Plugins:Watch", "false");
        builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new SettableClock(localTime)));
    }
}

/// <summary>The kernel and its real plugins, through the API.</summary>
public class ApiTests
{
    private static readonly DateTime Afternoon = new(2026, 10, 12, 14, 0, 0);
    private static readonly DateTime Night = new(2026, 10, 12, 23, 30, 0);

    private static Task<HttpResponseMessage> Transfer(HttpClient client, Guid target, decimal amount) =>
        client.PostAsJsonAsync("/transfers", new { source = DemoData.Alice, target, amount });

    [Fact]
    public async Task The_fees_plugin_applies_to_transfers()
    {
        await using var app = new BankWithPlugins(Afternoon);

        var response = await Transfer(app.CreateClient(), DemoData.Bob, 300m);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((0.30m, 699.70m), (body.GetProperty("fee").GetDecimal(), body.GetProperty("balance").GetDecimal()));
    }

    [Fact]
    public async Task The_blocklist_plugin_refuses_transfers_to_a_listed_account()
    {
        await using var app = new BankWithPlugins(Afternoon);

        var response = await Transfer(app.CreateClient(), DemoData.Carla, 10m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("[Blocklist]", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(14, HttpStatusCode.OK)]
    [InlineData(23, HttpStatusCode.BadRequest)]
    public async Task The_night_limit_plugin_depends_on_the_time(int hour, HttpStatusCode expected)
    {
        await using var app = new BankWithPlugins(new DateTime(2026, 10, 12, hour, 30, 0));

        var response = await app.CreateClient().PostAsJsonAsync($"/accounts/{DemoData.Alice}/withdrawal", new { amount = 250m });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task The_statement_formats_come_from_the_plugins()
    {
        await using var app = new BankWithPlugins(Afternoon);
        var client = app.CreateClient();

        var csv = await client.GetStringAsync($"/accounts/{DemoData.Alice}/statement?format=csv");
        var qif = await client.GetStringAsync($"/accounts/{DemoData.Alice}/statement?format=qif");
        var unknown = await client.GetAsync($"/accounts/{DemoData.Alice}/statement?format=pdf");

        Assert.StartsWith("date;label;amount;balance", csv);
        Assert.StartsWith("!Type:Bank", qif);
        Assert.Contains("\"csv\"", await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabling_a_plugin_changes_the_behaviour_without_restarting()
    {
        await using var app = new BankWithPlugins(Night);
        var client = app.CreateClient();

        await client.PostAsync("/admin/plugins/TransferFees/disable", null);
        var response = await Transfer(client, DemoData.Bob, 300m);

        Assert.Equal(0m, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fee").GetDecimal());
    }
}

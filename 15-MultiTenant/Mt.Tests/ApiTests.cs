using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Mt.Api;

namespace Mt.Tests;

/// <summary>The complete application: tenant identification, quotas, platform back office.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    /// <summary>A client that announces itself with the X-Tenant header.</summary>
    private HttpClient Client(string? tenant = null)
    {
        var client = _app.CreateClient();
        if (tenant is not null)
            client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        return client;
    }

    private async Task<decimal> Balance(string tenant, Guid account) =>
        (await Client(tenant).GetFromJsonAsync<JsonElement>($"/accounts/{account}")).GetProperty("balance").GetDecimal();

    private async Task<JsonElement> Storage() => await Client().GetFromJsonAsync<JsonElement>("/platform/storage");

    private static IEnumerable<string> TenantsOf(JsonElement storage, string databaseName) =>
        storage.EnumerateArray().Where(b => b.GetProperty("database").GetString() == databaseName)
            .SelectMany(b => b.GetProperty("tenants").EnumerateArray().Select(l => l.GetProperty("tenant").GetString()!));

    [Fact]
    public async Task The_same_URL_gives_a_different_answer_depending_on_the_tenant()
    {
        Assert.Equal(1_000m, await Balance("alpha", DemoData.Alice));
        Assert.Equal(3_000m, await Balance("beta", DemoData.Alice));
        Assert.Equal(25_000m, await Balance("gamma", DemoData.Alice));
    }

    [Fact]
    public async Task The_tenant_is_also_recognized_by_the_subdomain()
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://beta.localhost") });

        var response = await client.GetAsync($"/accounts/{DemoData.Alice}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("beta", response.Headers.GetValues("X-Tenant").Single());
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.BadRequest)]          // no tenant
    [InlineData("unknown", null, HttpStatusCode.NotFound)]       // tenant missing from the catalog
    [InlineData("alpha", "http://beta.localhost", HttpStatusCode.BadRequest)]  // header and subdomain contradict each other
    [InlineData("beta", "http://beta.localhost", HttpStatusCode.OK)]           // consistent: accepted
    public async Task A_request_without_a_clear_tenant_does_not_reach_the_business_code(string? header, string? address, HttpStatusCode expected)
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(address ?? "http://localhost") });
        if (header is not null)
            client.DefaultRequestHeaders.Add("X-Tenant", header);

        Assert.Equal(expected, (await client.GetAsync("/accounts")).StatusCode);
    }

    [Fact]
    public async Task Another_bank_s_account_answers_404_like_an_account_that_does_not_exist()
    {
        var response = await Client("alpha").PostAsJsonAsync("/accounts", new { owner = "Zoe", initialDeposit = 100 });
        var url = response.Headers.Location!.ToString();

        Assert.Equal(HttpStatusCode.OK, (await Client("alpha").GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client("beta").GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task The_transfer_rules_depend_on_the_bank()
    {
        var transfer = new { source = DemoData.Alice, target = DemoData.Bob, amount = 1_500 };

        Assert.Equal(HttpStatusCode.BadRequest, (await Client("alpha").PostAsJsonAsync("/transfers", transfer)).StatusCode);  // limit 1,000 €
        var beta = await (await Client("beta").PostAsJsonAsync("/transfers", transfer)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.5m, beta.GetProperty("fee").GetDecimal());
    }

    [Fact]
    public async Task A_suspended_tenant_is_refused_then_finds_its_data_again()
    {
        await Client().PostAsync("/platform/tenants/alpha/suspend", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("alpha").GetAsync("/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client("beta").GetAsync("/accounts")).StatusCode);

        await Client().PostAsync("/platform/tenants/alpha/reactivate", null);
        Assert.Equal(1_000m, await Balance("alpha", DemoData.Alice));
    }

    [Fact]
    public async Task A_noisy_neighbour_exhausts_its_quota_without_disturbing_the_others()
    {
        var alpha = Client("alpha");
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/tenant")).StatusCode);

        var refusal = await alpha.GetAsync("/tenant");

        Assert.Equal(HttpStatusCode.TooManyRequests, refusal.StatusCode);
        Assert.Contains("60 requests per minute", await refusal.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await Client("beta").GetAsync("/tenant")).StatusCode);
    }

    [Fact]
    public async Task A_new_customer_is_operational_as_soon_as_it_registers()
    {
        var definition = new
        {
            id = "delta", name = "Bank Delta", plan = "Premium",
            rules = new { transferLimit = 2_000, transferFee = 1, monthlyFee = 0, maxAccounts = 50 }
        };

        var registration = await Client().PostAsJsonAsync("/platform/tenants", definition);
        var opening = await Client("delta").PostAsJsonAsync("/accounts", new { owner = "Zoe", initialDeposit = 100 });

        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        Assert.Contains("delta", TenantsOf(await Storage(), "bank-delta"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsJsonAsync("/platform/tenants", definition)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client().PostAsJsonAsync("/platform/tenants", definition with { id = "Not Valid!" })).StatusCode);
    }

    [Fact]
    public async Task A_customer_leaving_erases_its_data_and_only_its_data()
    {
        var report = await (await Client().DeleteAsync("/platform/tenants/alpha")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, report.GetProperty("accounts").GetInt32());
        Assert.Equal(["beta"], TenantsOf(await Storage(), "bank-shared"));
        Assert.Equal(HttpStatusCode.NotFound, (await Client("alpha").GetAsync("/accounts")).StatusCode);
        Assert.Equal(3_000m, await Balance("beta", DemoData.Alice));
    }

    [Fact]
    public async Task Moving_to_premium_moves_the_data_to_a_dedicated_database()
    {
        await Client("alpha").PostAsJsonAsync($"/accounts/{DemoData.Alice}/withdrawal", new { amount = 100 });

        var response = await Client().PostAsJsonAsync("/platform/tenants/alpha/plan", new { plan = "Premium" });
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("bank-alpha", report.GetProperty("to").GetString());
        Assert.Equal(2, report.GetProperty("accounts").GetInt32());
        Assert.Equal(900m, await Balance("alpha", DemoData.Alice));
        Assert.Equal(2, (await Client("alpha").GetFromJsonAsync<JsonElement>($"/accounts/{DemoData.Alice}/operations")).GetArrayLength());  // initial deposit + withdrawal
        var storage = await Storage();
        Assert.DoesNotContain("alpha", TenantsOf(storage, "bank-shared"));
        Assert.Equal(["alpha"], TenantsOf(storage, "bank-alpha"));
        var quotas = (await Client("alpha").GetFromJsonAsync<JsonElement>("/tenant")).GetProperty("quotas");
        Assert.Equal(600, quotas.GetProperty("requestsPerMinute").GetInt32());
    }

    [Fact]
    public async Task The_monthly_fee_task_works_tenant_by_tenant()
    {
        await Client().PostAsync("/platform/tenants/beta/suspend", null);

        var reports = await (await Client().PostAsJsonAsync("/platform/tasks/monthly-fees", new { month = "2026-10" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        await Client().PostAsJsonAsync("/platform/tasks/monthly-fees", new { month = "2026-10" });  // replayed: no effect
        await Client().PostAsync("/platform/tenants/beta/reactivate", null);

        Assert.Equal(998m, await Balance("alpha", DemoData.Alice));    // 2 € at Alpha
        Assert.Equal(3_000m, await Balance("beta", DemoData.Alice));   // Beta suspended: skipped
        Assert.Equal(24_990m, await Balance("gamma", DemoData.Alice)); // 10 € at Gamma, in its own database
        Assert.Contains(reports.EnumerateArray(), b => b.GetProperty("tenant").GetString() == "beta"
                                                       && b.GetProperty("note").GetString() == "suspended: skipped");
    }
}

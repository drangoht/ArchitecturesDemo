using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pipeline.Bank;

namespace Pipeline.Tests;

/// <summary>The complete application: the file sent in the request body, read as a stream.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private static readonly string File = string.Join('\n',
        "reference;payer;payee;amount;currency",
        $"A-1;{DemoData.Alice};{DemoData.Bob};100;EUR",
        $"A-2;{DemoData.Alice};{DemoData.Bob};abc;EUR",
        $"A-3;{DemoData.Bob};{DemoData.Alice};50;EUR");

    private async Task<(HttpStatusCode Code, JsonElement Import)> Import(string request = "/imports")
    {
        var response = await _client.PostAsync(request, new StringContent(File));
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private async Task<decimal> Balance(string iban) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/accounts/{iban}")).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task An_import_executes_the_file_and_keeps_its_report()
    {
        var (code, import) = await Import();

        Assert.Equal(HttpStatusCode.OK, code);
        var report = import.GetProperty("report");
        Assert.Equal((3, 2, 1), (report.GetProperty("lines").GetInt32(), report.GetProperty("outputs").GetInt32(), report.GetProperty("rejections").GetInt32()));
        Assert.Equal(950m, await Balance(DemoData.Alice));
        var id = import.GetProperty("id").GetInt32();
        Assert.Equal("Completed", (await _client.GetFromJsonAsync<JsonElement>($"/imports/{id}")).GetProperty("report").GetProperty("status").GetString());
        Assert.Single((await _client.GetFromJsonAsync<JsonElement>("/imports")).EnumerateArray());
    }

    [Fact]
    public async Task A_dry_run_import_touches_no_account()
    {
        var (_, import) = await Import("/imports?dryRun=true");

        Assert.True(import.GetProperty("dryRun").GetBoolean());
        Assert.Equal(2, import.GetProperty("report").GetProperty("outputs").GetInt32());
        Assert.Equal(1_000m, await Balance(DemoData.Alice));
    }

    [Fact]
    public async Task A_failure_answers_500_with_the_report()
    {
        var (code, import) = await Import("/imports?failureAtReference=A-3");

        Assert.Equal(HttpStatusCode.InternalServerError, code);
        Assert.Equal("Failed", import.GetProperty("report").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/accounts/FR00UNKNOWN")).StatusCode);
    }

    [Fact]
    public async Task The_bulk_import_demo_points_out_the_bottleneck()
    {
        var report = await (await _client.PostAsJsonAsync("/demo/bulk-import", new { lines = 5_000, fraudParallelism = 2 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Completed", report.GetProperty("status").GetString());
        Assert.Equal(6, report.GetProperty("stages").GetArrayLength());
        Assert.Equal("Check fraud", report.GetProperty("bottleneck").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/demo/bulk-import", new { lines = 0 })).StatusCode);
    }
}

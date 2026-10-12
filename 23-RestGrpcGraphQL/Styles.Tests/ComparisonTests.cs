using Styles.Client;

namespace Styles.Tests;

/// <summary>The same uses, through the three APIs: same result, different costs.</summary>
public class ComparisonTests : IAsyncLifetime
{
    private readonly TestSystem _system = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _system.DisposeAsync();

    private async Task<(HomeScreen Screen, Measure Measure, long Reads)> Screen(IBankClient client, HttpCounter counter, string customerId)
    {
        var before = _system.Bank.Statistics.OperationReads;
        var (screen, measure) = await Timing.Run(counter, () => client.HomeScreen(customerId));
        return (screen, measure, _system.Bank.Statistics.OperationReads - before);
    }

    [Fact]
    public async Task The_three_APIs_give_exactly_the_same_screen()
    {
        // An operation dated "now" (with milliseconds): each API has its precision for dates.
        await _system.Rest.Transfer("20001", "10001", 19.99m, "Refund");

        var rest = await _system.Rest.HomeScreen("alice");
        var graphql = await _system.GraphQL.HomeScreen("alice");
        var grpc = await _system.Grpc.HomeScreen("alice");

        foreach (var other in new[] { graphql, grpc })
        {
            Assert.Equal(rest.Name, other.Name);
            Assert.Equal(rest.Accounts.Select(c => (c.Number, c.Label, c.Balance)), other.Accounts.Select(c => (c.Number, c.Label, c.Balance)));
            Assert.Equal(rest.Accounts.SelectMany(c => c.Operations), other.Accounts.SelectMany(c => c.Operations));
        }
        Assert.Equal(1646.04m, rest.Accounts[0].Balance);
        Assert.Equal("Refund from 20001", rest.Accounts[0].Operations[0].Label);
    }

    [Fact]
    public async Task REST_makes_one_request_per_account_GraphQL_and_gRPC_a_single_one_in_all()
    {
        Assert.Equal(5, (await Screen(_system.Rest, _system.RestCounter, "alice")).Measure.Requests);   // customer + accounts + 3 × operations
        Assert.Equal(1, (await Screen(_system.GraphQL, _system.GraphQLCounter, "alice")).Measure.Requests);
        Assert.Equal(1, (await Screen(_system.Grpc, _system.GrpcCounter, "alice")).Measure.Requests);

        Assert.Equal(3, (await Screen(_system.Rest, _system.RestCounter, "bob")).Measure.Requests);     // a single account
    }

    [Fact]
    public async Task Behind_the_single_GraphQL_request_the_database_is_read_only_once()
    {
        Assert.Equal(3, (await Screen(_system.Rest, _system.RestCounter, "alice")).Reads);
        Assert.Equal(1, (await Screen(_system.GraphQL, _system.GraphQLCounter, "alice")).Reads);
        Assert.Equal(1, (await Screen(_system.Grpc, _system.GrpcCounter, "alice")).Reads);
    }

    [Fact]
    public async Task gRPC_transfers_the_fewest_bytes_and_REST_the_most()
    {
        var rest = (await Screen(_system.Rest, _system.RestCounter, "alice")).Measure;
        var graphql = (await Screen(_system.GraphQL, _system.GraphQLCounter, "alice")).Measure;
        var grpc = (await Screen(_system.Grpc, _system.GrpcCounter, "alice")).Measure;

        // Received: REST sends complete representations, GraphQL only the requested fields (in JSON, names included),
        // gRPC field numbers and binary integers.
        Assert.True(grpc.BytesReceived < graphql.BytesReceived, $"gRPC {grpc.BytesReceived} B, GraphQL {graphql.BytesReceived} B");
        Assert.True(graphql.BytesReceived * 2 < rest.BytesReceived, $"GraphQL {graphql.BytesReceived} B, REST {rest.BytesReceived} B");
        // Sent: REST sends nothing (GETs), GraphQL sends the text of its query, gRPC a small binary message.
        Assert.Equal(0, rest.BytesSent);
        Assert.True(grpc.BytesSent < 20 && graphql.BytesSent > 150, $"gRPC {grpc.BytesSent} B, GraphQL {graphql.BytesSent} B");
    }

    [Theory]
    [InlineData("20001", "10001", 1_000_000, "422 INSUFFICIENT_BALANCE", "INSUFFICIENT_BALANCE", "FailedPrecondition")]
    [InlineData("20001", "99999", 10, "404 UNKNOWN_ACCOUNT", "UNKNOWN_ACCOUNT", "NotFound")]
    [InlineData("20001", "10001", -5, "400 INVALID_AMOUNT", "INVALID_AMOUNT", "InvalidArgument")]
    public async Task The_same_business_error_in_the_language_of_each_API(string source, string target, decimal amount,
        string rest, string graphql, string grpc)
    {
        var results = new List<TransferResult>();
        foreach (var client in _system.AllThree)
            results.Add(await client.Transfer(source, target, amount, "Test"));

        Assert.Equal([rest, graphql, grpc], results.Select(r => r.Error));
        Assert.Single(results.Select(r => r.Message).Distinct());   // the readable message, however, is the same everywhere
    }

    [Fact]
    public async Task A_transfer_succeeds_the_same_way_through_the_three_APIs()
    {
        var balances = new List<decimal?>();
        foreach (var client in _system.AllThree)
            balances.Add((await client.Transfer("10001", "20001", 100m, $"Via {client.Name}")).SourceBalance);

        Assert.Equal([1526.05m, 1426.05m, 1326.05m], balances);
        Assert.Equal(1460.01m, _system.Bank.Account("20001").Balance);
    }
}

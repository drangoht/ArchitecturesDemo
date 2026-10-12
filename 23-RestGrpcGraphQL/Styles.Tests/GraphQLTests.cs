using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Styles.Tooling.GraphQL;

namespace Styles.Tests;

public class GraphQLTests : IAsyncLifetime
{
    private readonly TestSystem _system = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _system.DisposeAsync();

    private static string[] Keys(JsonNode? obj) => obj!.AsObject().Select(p => p.Key).ToArray();

    private static string Code(JsonObject response, int index = 0) => response["errors"]![index]!["extensions"]!["code"]!.GetValue<string>();

    [Fact]
    public void The_client_gets_exactly_the_requested_fields_and_nothing_else()
    {
        var response = _system.Query("""{ account(number: "10001") { label balance } }""");

        Assert.Equal(["label", "balance"], Keys(response["data"]!["account"]));
        Assert.Null(response["errors"]);
    }

    [Fact]
    public void Aliases_allow_asking_for_the_same_field_twice()
    {
        var response = _system.Query("""{ current: account(number: "10001") { balance } savings: account(number: "10002") { balance } __typename }""");

        Assert.Equal(["current", "savings", "__typename"], Keys(response["data"]));
        Assert.Equal(8150.0, response["data"]!["savings"]!["balance"]!.GetValue<double>());
        Assert.Equal("Query", response["data"]!["__typename"]!.GetValue<string>());
    }

    [Fact]
    public void The_operations_of_all_the_accounts_of_a_level_are_read_at_once()
    {
        var before = _system.Bank.Statistics.OperationReads;
        var response = _system.Query("""{ customer(id: "alice") { accounts { label operations(latest: 2) { label } } } }""");

        Assert.Equal(3, response["data"]!["customer"]!["accounts"]!.AsArray().Count);
        Assert.Equal(1, _system.Bank.Statistics.OperationReads - before);   // and not 3: the resolver is batched

        // Even one level deeper, on 9 accounts (3 accounts × their owners × 3 accounts), a single read.
        before = _system.Bank.Statistics.OperationReads;
        _system.Query("""{ customer(id: "alice") { accounts { owner { accounts { operations { id } } } } } }""");
        Assert.Equal(1, _system.Bank.Statistics.OperationReads - before);
    }

    [Fact]
    public void A_query_that_is_too_deep_is_refused_before_being_executed()
    {
        var before = _system.Bank.Statistics.OperationReads;
        var response = _system.Query("""{ customer(id: "alice") { accounts { owner { accounts { owner { accounts { operations { id } } } } } } } }""");

        Assert.Null(response["data"]);
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", Code(response));
        Assert.Contains("maximum depth of 6", response["errors"]![0]!["message"]!.GetValue<string>());
        Assert.Equal(before, _system.Bank.Statistics.OperationReads);   // nothing was executed
    }

    [Fact]
    public void An_unknown_field_or_argument_is_refused_by_the_validation_with_its_position()
    {
        var response = _system.Query("""
            {
              account(number: "10001") { label balances }
              customer(identifier: "alice") { name }
            }
            """);

        Assert.False(response.ContainsKey("data"));   // an invalid query is not executed at all
        var errors = response["errors"]!.AsArray();
        Assert.Equal(3, errors.Count);   // the unknown field, the unknown argument, the missing required argument
        Assert.Equal("The field \"balances\" does not exist on type \"Account\".", errors[0]!["message"]!.GetValue<string>());
        Assert.Equal((2, 36), (errors[0]!["locations"]![0]!["line"]!.GetValue<int>(), errors[0]!["locations"]![0]!["column"]!.GetValue<int>()));
        Assert.Contains("\"id\" is required", errors[2]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void A_syntax_error_gives_the_line_and_the_column()
    {
        var response = _system.Query("{\n  account(number: \"10001\" { label }\n}");

        Assert.Equal("GRAPHQL_PARSE_FAILED", Code(response));
        Assert.Equal(2, response["errors"]![0]!["locations"]![0]!["line"]!.GetValue<int>());
    }

    [Fact]
    public void An_error_on_a_field_does_not_prevent_getting_the_others()
    {
        var response = _system.Query("""{ a: account(number: "10001") { label } b: account(number: "99999") { label } }""");

        Assert.Equal("Current account", response["data"]!["a"]!["label"]!.GetValue<string>());
        Assert.Null(response["data"]!["b"]);
        var error = Assert.Single(response["errors"]!.AsArray())!;
        Assert.Equal("UNKNOWN_ACCOUNT", error["extensions"]!["code"]!.GetValue<string>());
        Assert.Equal("[\"b\"]", error["path"]!.ToJsonString());
    }

    [Fact]
    public void A_non_null_field_in_error_nulls_the_first_nullable_parent()
    {
        // operations is "[Operation!]!" and fails (51 > 50): the account cannot be null ("[Account!]!"),
        // neither can the list… the nullity goes up to "customer", the first field that accepts it.
        var response = _system.Query("""{ customer(id: "alice") { name accounts { operations(latest: 51) { id } } } }""");

        Assert.Null(response["data"]!["customer"]);
        var errors = response["errors"]!.AsArray();
        Assert.Equal(3, errors.Count);   // one per account
        Assert.Equal("[\"customer\",\"accounts\",0,\"operations\"]", errors[0]!["path"]!.ToJsonString());
        Assert.All(errors, e => Assert.Equal("INVALID_PARAMETER", e!["extensions"]!["code"]!.GetValue<string>()));
    }

    [Fact]
    public void The_variables_are_checked_according_to_their_type()
    {
        const string query = "query ($number: ID!, $n: Int) { account(number: $number) { operations(latest: $n) { id } } }";

        var correct = _system.Query(query, new { number = "10001", n = 2 });
        Assert.Equal(2, correct["data"]!["account"]!["operations"]!.AsArray().Count);

        var withoutN = _system.Query(query, new { number = "10001" });   // $n missing: the argument keeps its default (10)
        Assert.Equal(6, withoutN["data"]!["account"]!["operations"]!.AsArray().Count);

        Assert.Equal("BAD_USER_INPUT", Code(_system.Query(query, new { number = "10001", n = "three" })));
        Assert.Equal("BAD_USER_INPUT", Code(_system.Query(query, new { n = 2 })));
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", Code(_system.Query("query ($x: Int) { account(number: \"10001\") { label } }")));
        Assert.Equal("GRAPHQL_VALIDATION_FAILED", Code(_system.Query("query ($n: String) { account(number: \"10001\") { operations(latest: $n) { id } } }")));
    }

    [Fact]
    public void A_mutation_is_executed_and_returns_what_the_client_asks_for()
    {
        var response = _system.Query("""
            mutation { makeTransfer(source: "10001", target: "20001", amount: 25.5, label: "Cinema") { id amount source { balance } target { balance } } }
            """);

        var transfer = response["data"]!["makeTransfer"]!;
        Assert.Equal("V001", transfer["id"]!.GetValue<string>());
        Assert.Equal(1600.55, transfer["source"]!["balance"]!.GetValue<double>(), 2);
        Assert.Equal(1185.51, transfer["target"]!["balance"]!.GetValue<double>(), 2);
    }

    [Fact]
    public async Task The_published_schema_flags_the_deprecated_fields_and_the_server_knows_who_uses_them()
    {
        var sdl = await _system.Http.GetStringAsync("/graphql/schema");
        Assert.Contains("balanceText: String! @deprecated(reason: \"Use 'balance' (a number) and format it for display.\")", sdl);
        Assert.Contains("operations(latest: Int = 10): [Operation!]!", sdl);

        Assert.False(_system.Engine.Usage.ContainsKey("Account.balanceText"));
        var response = _system.Query("""{ account(number: "10002") { balanceText } }""");
        Assert.Equal("8,150.00 €", response["data"]!["account"]!["balanceText"]!.GetValue<string>());
        Assert.Equal(1, _system.Engine.Usage["Account.balanceText"]);
    }

    [Fact]
    public void An_unexpected_error_discloses_nothing_to_the_client()
    {
        var schema = new Schema();
        schema.Query.Field("fragile", "String", _ => throw new InvalidOperationException("database password: secret"));
        var response = new GraphQLEngine(schema).Execute("{ fragile }");

        Assert.Null(response["data"]!["fragile"]);
        Assert.Equal("Internal error.", response["errors"]![0]!["message"]!.GetValue<string>());
        Assert.DoesNotContain("secret", response.ToJsonString());
    }

    [Fact]
    public async Task Over_HTTP_everything_goes_through_POST_graphql_and_answers_200_even_on_error()
    {
        var response = await _system.Http.PostAsJsonAsync("/graphql", new { query = """{ account(number: "99999") { label } }""" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("UNKNOWN_ACCOUNT", Code(body));
    }
}

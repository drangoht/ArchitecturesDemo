using System.Globalization;
using System.Text.Json.Nodes;
using Styles.Bank;
using Styles.Tooling.GraphQL;

namespace Styles.Server;

/// <summary>
/// The GraphQL API: a GRAPH of types (Customer → accounts → operations…) and a single address, /graphql. It is the CLIENT
/// that writes its query: it asks exactly for the fields it will display, over several levels, in one round trip.
/// The price: the server must protect itself from expensive queries (depth, N+1 in the database) and the HTTP cache is no longer of use.
/// </summary>
public static class GraphQLApi
{
    public static Schema Schema(BankService bank)
    {
        var schema = new Schema();
        schema.Enum("AccountType", "CURRENT", "SAVINGS");

        schema.Query
            .Field("customer", "Customer", (_, a) => bank.Customer(a.Get<string>("id")), new Arg("id", "ID!"))
            .Field("account", "Account", (_, a) => bank.Account(a.Get<string>("number")), new Arg("number", "ID!"));

        schema.DeclareMutation()
            .Field("makeTransfer", "Transfer!",
                (_, a) => bank.MakeTransfer(a.Get<string>("source"), a.Get<string>("target"),
                    (decimal)a.Get<double>("amount"), a.GetOrDefault<string>("label")),
                new Arg("source", "ID!"), new Arg("target", "ID!"), new Arg("amount", "Float!"), new Arg("label", "String"));

        schema.Object<Customer>("Customer")
            .Field("id", "ID!", c => c.Id)
            .Field("name", "String!", c => c.Name)
            .Field("email", "String!", c => c.Email)
            .Field("accounts", "[Account!]!", c => bank.AccountsOf(c.Id));

        schema.Object<Account>("Account")
            .Field("number", "ID!", c => c.Number)
            .Field("label", "String!", c => c.Label)
            .Field("type", "AccountType!", c => c.Type)
            .Field("iban", "String!", c => c.Iban)
            .Field("balance", "Float!", c => c.Balance)
            // The first version of the API returned the balance already formatted. The field is still served for the old clients;
            // the usage statistic (/graphql/usage) will tell when nobody asks for it any more.
            .Field("balanceText", "String!", c => c.Balance.ToString("N2", CultureInfo.GetCultureInfo("en-GB")) + " €")
            .Deprecated("Use 'balance' (a number) and format it for display.")
            // BATCHED resolver: for all the accounts of a level, ONE database read, instead of one per account.
            .BatchedField("operations", "[Operation!]!", (accounts, a) =>
            {
                var byAccount = bank.LatestOperations(accounts.Select(c => c.Number).ToList(), a.GetOrDefault<int?>("latest") ?? 10);
                return accounts.Select(c => (object?)byAccount[c.Number]).ToList();
            }, new Arg("latest", "Int", 10))
            .Field("owner", "Customer!", c => bank.Customer(c.CustomerId));

        schema.Object<Operation>("Operation")
            .Field("id", "ID!", o => o.Id)
            .Field("date", "String!", o => o.Date.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture))
            .Field("label", "String!", o => o.Label)
            .Field("amount", "Float!", o => o.Amount);

        schema.Object<Transfer>("Transfer")
            .Field("id", "ID!", v => v.Id)
            .Field("amount", "Float!", v => v.Amount)
            .Field("label", "String!", v => v.Label)
            .Field("source", "Account!", v => bank.Account(v.Source))
            .Field("target", "Account!", v => bank.Account(v.Target));

        return schema;
    }

    public static GraphQLEngine Engine(BankService bank) => new(Schema(bank), new GraphQLOptions
    {
        MaxDepth = 6,
        TranslateError = e => e is BusinessError m ? new GraphQLError(m.Message, Translations.GraphQLCode(m.Code)) : null
    });

    public sealed record GraphQLRequest(string? Query, JsonObject? Variables, string? OperationName);

    public static void MapGraphQL(this IEndpointRouteBuilder app)
    {
        // A single address, always POST, always 200 as soon as the query was understood: the result (and the errors)
        // is in the body. The HTTP tools (caches, access logs, alerts on 5xx) no longer see anything.
        app.MapPost("/graphql", (GraphQLRequest request, GraphQLEngine engine) =>
            Results.Json(engine.Execute(request.Query, request.Variables, request.OperationName)));

        app.MapGet("/graphql/schema", (GraphQLEngine engine) => Results.Text(engine.Schema.ToSdl(), "text/plain; charset=utf-8"));

        app.MapGet("/graphql/usage", (GraphQLEngine engine) => Results.Ok(engine.Usage));
    }
}

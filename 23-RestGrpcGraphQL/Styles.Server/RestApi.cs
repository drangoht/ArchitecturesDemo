using Styles.Bank;

namespace Styles.Server;

/// <summary>
/// The REST API: RESOURCES (customers, accounts, operations, transfers), each at its URL, handled with the HTTP
/// verbs. Each resource has ONE representation, complete, the same for all clients: the screen that only wants the
/// balance also receives the IBAN, the owner, the links… (over-fetching), and the screen that wants the accounts AND their
/// operations must make several calls (under-fetching). In exchange: HTTP does the rest (cache, codes, tools).
/// </summary>
public static class RestApi
{
    public static void MapRest(this IEndpointRouteBuilder app)
    {
        var rest = app.MapGroup("").AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (BusinessError e)   // a business error becomes an HTTP status and an "application/problem+json" body
            {
                return Results.Problem(statusCode: Translations.HttpStatus(e.Code), title: e.Message,
                    extensions: new Dictionary<string, object?> { ["code"] = Translations.GraphQLCode(e.Code) });
            }
        });

        rest.MapGet("/customers/{id}", (string id, BankService bank) =>
        {
            var customer = bank.Customer(id);
            return Results.Ok(new
            {
                id = customer.Id,
                name = customer.Name,
                email = customer.Email,
                _links = new { self = $"/customers/{id}", accounts = $"/customers/{id}/accounts" }
            });
        });

        rest.MapGet("/customers/{id}/accounts", (string id, BankService bank) =>
            Results.Ok(bank.AccountsOf(id).Select(c => Representation(c, bank))));

        // GET is safe and idempotent: it can be cached and revalidated. The ETag changes at each operation of the account;
        // a client that polls often sends back the ETag it has, and receives "304 Not Modified" (without body) if nothing moved.
        rest.MapGet("/accounts/{number}", (string number, BankService bank, HttpContext http) =>
        {
            var account = bank.Account(number);
            var etag = $"\"{account.Number}-v{account.Version}\"";
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "private, no-cache";   // keep it, but revalidate before use
            return http.Request.Headers.IfNoneMatch == etag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(Representation(account, bank));
        });

        rest.MapGet("/accounts/{number}/operations", (string number, int? limit, BankService bank) =>
            Results.Ok(bank.LatestOperations(number, limit ?? 10).Select(o => new
            {
                id = o.Id,
                date = o.Date,
                label = o.Label,
                amount = o.Amount,
                currency = "EUR"
            })));

        // POST is not idempotent: it creates a new resource, whose address is returned in "Location".
        rest.MapPost("/transfers", (RestTransferRequest request, BankService bank) =>
        {
            var transfer = bank.MakeTransfer(request.Source, request.Target, request.Amount, request.Label);
            return Results.Created($"/transfers/{transfer.Id}", Representation(transfer));
        });

        rest.MapGet("/transfers/{id}", (string id, BankService bank) => Results.Ok(Representation(bank.Transfer(id))));
    }

    public sealed record RestTransferRequest(string Source, string Target, decimal Amount, string? Label);

    private static object Representation(Account account, BankService bank)
    {
        var owner = bank.Customer(account.CustomerId);
        return new
        {
            number = account.Number,
            label = account.Label,
            type = account.Type.ToString().ToLowerInvariant(),
            iban = account.Iban,
            balance = account.Balance,
            currency = "EUR",
            openingDate = account.OpeningDate,
            overdraftLimit = account.OverdraftLimit,
            owner = new { id = owner.Id, name = owner.Name },
            _links = new
            {
                self = $"/accounts/{account.Number}",
                operations = $"/accounts/{account.Number}/operations",
                owner = $"/customers/{owner.Id}"
            }
        };
    }

    private static object Representation(Transfer transfer) => new
    {
        id = transfer.Id,
        source = transfer.Source,
        target = transfer.Target,
        amount = transfer.Amount,
        label = transfer.Label,
        date = transfer.Date,
        sourceBalance = transfer.SourceBalance,
        _links = new
        {
            self = $"/transfers/{transfer.Id}",
            source = $"/accounts/{transfer.Source}",
            target = $"/accounts/{transfer.Target}"
        }
    };
}

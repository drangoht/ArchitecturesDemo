namespace Cell.Router;

/// <summary>
/// THE DATA PLANE: read the partition key, find the cell, relay. Nothing else: no business logic, no database,
/// no aggregation. The router is the only component shared by all the cells; the simpler it is, the less likely it is
/// to go down (and drag everything down with it).
/// </summary>
public sealed class Relay(PlacementTable placement, CellRegistry registry, IHttpClientFactory factory)
{
    public async Task<IResult> Forward(string customer, HttpContext http)
    {
        if (placement.CellOf(customer) is not { } id || registry.Find(id) is not { } cell)
            return Results.NotFound(new { error = $"Unknown customer \"{customer}\"." });
        if (placement.IsMigrating(customer))
        {
            http.Response.Headers.RetryAfter = "2";
            return Results.Json(new { error = $"Customer \"{customer}\" is changing cell: try again in a moment." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        using var request = new HttpRequestMessage(new HttpMethod(http.Request.Method),
            new Uri(new Uri(cell.Url), http.Request.Path + http.Request.QueryString));
        // Body present: known length, or chunked sending (JSON serialized on the fly, without Content-Length).
        var method = http.Request.Method;
        if (http.Request.ContentLength > 0 || http.Request.ContentLength is null && (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method)))
        {
            request.Content = new StreamContent(http.Request.Body);
            if (http.Request.ContentType is { } type)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", type);
        }

        HttpResponseMessage response;
        try
        {
            response = await factory.CreateClient($"cell-{cell.Id}").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, http.RequestAborted);
        }
        catch (HttpRequestException)
        {
            // Blast radius: only the customers of THIS cell get this error.
            http.Response.Headers["X-Cell"] = cell.Id;
            return Results.Json(new { error = $"Cell {cell.Id} unreachable: only its customers are affected." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        using (response)
        {
            http.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in new[] { "X-Cell", "X-Cell-Version", "Retry-After" })
                if (response.Headers.TryGetValues(header, out var values))
                    http.Response.Headers[header] = values.ToArray();
            if (response.Content.Headers.ContentType is { } content)
                http.Response.ContentType = content.ToString();
            await response.Content.CopyToAsync(http.Response.Body, http.RequestAborted);
        }
        return Results.Empty;
    }
}

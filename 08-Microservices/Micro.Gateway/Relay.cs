namespace Micro.Gateway;

/// <summary>Marker type of the service (used by the tests to start THIS service).</summary>
public sealed class Marker;

/// <summary>Minimal reverse proxy: forwards the request to the right service and copies its response back.</summary>
internal static class Relay
{
    public static async Task Forward(HttpContext context, IReadOnlyDictionary<string, string> routes, IHttpClientFactory http)
    {
        var path = context.Request.Path.Value ?? "/";
        var route = routes.FirstOrDefault(r => path.StartsWith(r.Key, StringComparison.OrdinalIgnoreCase));
        if (route.Key is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var request = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            route.Value + path + context.Request.QueryString);

        var hasBody = context.Request.ContentLength > 0
                       || context.Request.Headers.TransferEncoding.Count > 0
                       || context.Request.ContentType is not null;
        if (hasBody)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is { } type)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", type);
        }

        try
        {
            using var response = await http.CreateClient("downstream")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } responseType)
                context.Response.ContentType = responseType.ToString();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new { error = $"The service behind {route.Key} is unreachable." });
        }
    }
}

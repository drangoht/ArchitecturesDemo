using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Styles.Tooling.Grpc;

/// <summary>
/// Hosting gRPC methods in ASP.NET Core (what Grpc.AspNetCore does): one POST route per method, the
/// reading of the framed request, the writing of the responses, and the status in the trailers, whether there was an error or not.
/// </summary>
public static class GrpcHost
{
    public static IEndpointConventionBuilder MapGrpcUnary<TRequest, TResponse>(this IEndpointRouteBuilder app, string service,
        string method, Func<TRequest, CancellationToken, Task<TResponse>> handler) where TRequest : new() where TResponse : notnull =>
        app.MapGrpcServerStream<TRequest, TResponse>(service, method,
            async (request, write, cancellation) => await write(await handler(request, cancellation)));

    public static IEndpointConventionBuilder MapGrpcServerStream<TRequest, TResponse>(this IEndpointRouteBuilder app, string service,
        string method, Func<TRequest, Func<TResponse, Task>, CancellationToken, Task> handler) where TRequest : new() where TResponse : notnull =>
        app.MapPost($"/{service}/{method}", http => Handle(http, handler));

    private static async Task Handle<TRequest, TResponse>(HttpContext http, Func<TRequest, Func<TResponse, Task>, CancellationToken, Task> handler)
        where TRequest : new() where TResponse : notnull
    {
        if (http.Request.ContentType?.StartsWith(GrpcFrame.ContentType, StringComparison.Ordinal) != true)
        {
            http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        if (!http.Response.SupportsTrailers())
        {
            // Without trailers (HTTP/1.1), no final status: that is why gRPC requires HTTP/2 (and gRPC-Web exists for browsers).
            http.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await http.Response.WriteAsync("gRPC requires HTTP/2.");
            return;
        }

        // The deadline set by the client (grpc-timeout): past this delay, the work is cancelled on the server side too.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        if (GrpcFrame.ReadTimeout(http.Request.Headers["grpc-timeout"]) is { } timeout)
            deadline.CancelAfter(timeout);

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = GrpcFrame.ContentType;

        var (status, message) = (GrpcStatus.Ok, "");
        try
        {
            var bytes = await GrpcFrame.Read(http.Request.Body, deadline.Token)
                ?? throw new GrpcError(GrpcStatus.InvalidArgument, "Request missing.");
            var request = Protobuf.Protobuf.Deserialize<TRequest>(bytes);
            await handler(request,
                response => GrpcFrame.Write(http.Response.Body, Protobuf.Protobuf.Serialize(response), deadline.Token),
                deadline.Token);
        }
        catch (GrpcError e)
        {
            (status, message) = (e.Status, e.Message);
        }
        catch (FormatException e)
        {
            (status, message) = (GrpcStatus.InvalidArgument, $"Invalid Protobuf message: {e.Message}");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            (status, message) = http.RequestAborted.IsCancellationRequested
                ? (GrpcStatus.Cancelled, "Call cancelled by the client.")
                : (GrpcStatus.DeadlineExceeded, "Deadline exceeded.");
        }
        catch (Exception e)
        {
            http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(GrpcHost)).LogError(e, "Error in a gRPC method");
            (status, message) = (GrpcStatus.Internal, "Internal error.");   // the detail stays in the server's log
        }

        http.Response.AppendTrailer("grpc-status", ((int)status).ToString());
        if (message.Length > 0)
            http.Response.AppendTrailer("grpc-message", Uri.EscapeDataString(message));
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Styles.Outillage.Grpc;

/// <summary>
/// L'hébergement de méthodes gRPC dans ASP.NET Core (ce que fait Grpc.AspNetCore) : une route POST par méthode, la
/// lecture de la demande encadrée, l'écriture des réponses, et le statut dans les trailers, qu'il y ait eu erreur ou non.
/// </summary>
public static class HoteGrpc
{
    public static IEndpointConventionBuilder MapGrpcUnaire<TDemande, TReponse>(this IEndpointRouteBuilder app, string service,
        string methode, Func<TDemande, CancellationToken, Task<TReponse>> traitement) where TDemande : new() where TReponse : notnull =>
        app.MapGrpcFluxServeur<TDemande, TReponse>(service, methode,
            async (demande, ecrire, annulation) => await ecrire(await traitement(demande, annulation)));

    public static IEndpointConventionBuilder MapGrpcFluxServeur<TDemande, TReponse>(this IEndpointRouteBuilder app, string service,
        string methode, Func<TDemande, Func<TReponse, Task>, CancellationToken, Task> traitement) where TDemande : new() where TReponse : notnull =>
        app.MapPost($"/{service}/{methode}", http => Traiter(http, traitement));

    private static async Task Traiter<TDemande, TReponse>(HttpContext http, Func<TDemande, Func<TReponse, Task>, CancellationToken, Task> traitement)
        where TDemande : new() where TReponse : notnull
    {
        if (http.Request.ContentType?.StartsWith(TrameGrpc.TypeDeContenu, StringComparison.Ordinal) != true)
        {
            http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }
        if (!http.Response.SupportsTrailers())
        {
            // Sans trailers (HTTP/1.1), pas de statut final : c'est pourquoi gRPC exige HTTP/2 (et gRPC-Web existe pour les navigateurs).
            http.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await http.Response.WriteAsync("gRPC demande HTTP/2.");
            return;
        }

        // L'échéance fixée par le client (grpc-timeout) : passé ce délai, le travail est annulé côté serveur aussi.
        using var echeance = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        if (TrameGrpc.LireDelai(http.Request.Headers["grpc-timeout"]) is { } delai)
            echeance.CancelAfter(delai);

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = TrameGrpc.TypeDeContenu;

        var (statut, message) = (StatutGrpc.Ok, "");
        try
        {
            var octets = await TrameGrpc.Lire(http.Request.Body, echeance.Token)
                ?? throw new ErreurGrpc(StatutGrpc.InvalidArgument, "Demande absente.");
            var demande = Protobuf.Protobuf.Deserialiser<TDemande>(octets);
            await traitement(demande,
                reponse => TrameGrpc.Ecrire(http.Response.Body, Protobuf.Protobuf.Serialiser(reponse), echeance.Token),
                echeance.Token);
        }
        catch (ErreurGrpc e)
        {
            (statut, message) = (e.Statut, e.Message);
        }
        catch (FormatException e)
        {
            (statut, message) = (StatutGrpc.InvalidArgument, $"Message Protobuf invalide : {e.Message}");
        }
        catch (OperationCanceledException) when (echeance.IsCancellationRequested)
        {
            (statut, message) = http.RequestAborted.IsCancellationRequested
                ? (StatutGrpc.Cancelled, "Appel annulé par le client.")
                : (StatutGrpc.DeadlineExceeded, "Échéance dépassée.");
        }
        catch (Exception e)
        {
            http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(HoteGrpc)).LogError(e, "Erreur dans une méthode gRPC");
            (statut, message) = (StatutGrpc.Internal, "Erreur interne.");   // le détail reste dans le journal du serveur
        }

        http.Response.AppendTrailer("grpc-status", ((int)statut).ToString());
        if (message.Length > 0)
            http.Response.AppendTrailer("grpc-message", Uri.EscapeDataString(message));
    }
}

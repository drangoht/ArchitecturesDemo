using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Styles.Outillage.Protobuf;

namespace Styles.Outillage.Grpc;

/// <summary>
/// Un client gRPC : une méthode, c'est un POST HTTP/2 sur « /paquet.Service/Methode », le message encadré dans le corps,
/// et le résultat lu dans les trailers. En vrai : Grpc.Net.Client et le client typé généré par protoc.
/// </summary>
public sealed class ClientGrpc(HttpClient http, string service)
{
    /// <summary>Un appel unaire : une demande, une réponse.</summary>
    public async Task<TReponse> Unaire<TDemande, TReponse>(string methode, TDemande demande, TimeSpan? delai = null,
        CancellationToken annulation = default) where TDemande : notnull where TReponse : new()
    {
        var reponses = new List<TReponse>();
        await foreach (var reponse in Flux<TDemande, TReponse>(methode, demande, delai, annulation))
            reponses.Add(reponse);   // on lit jusqu'au bout : le statut n'arrive qu'après, dans les trailers
        return reponses.Count == 1
            ? reponses[0]
            : throw new ErreurGrpc(StatutGrpc.Internal, $"{reponses.Count} réponses reçues pour un appel unaire.");
    }

    /// <summary>Un flux serveur : une demande, des réponses au fil de l'eau, puis le statut final.</summary>
    public async IAsyncEnumerable<TReponse> Flux<TDemande, TReponse>(string methode, TDemande demande, TimeSpan? delai = null,
        [EnumeratorCancellation] CancellationToken annulation = default) where TDemande : notnull where TReponse : new()
    {
        using var requete = new HttpRequestMessage(HttpMethod.Post, $"/{service}/{methode}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,   // HTTP/2 obligatoire (en clair : « h2c »)
            Content = new ByteArrayContent(TrameGrpc.Encadrer(Protobuf.Protobuf.Serialiser(demande)))
        };
        requete.Content.Headers.ContentType = new MediaTypeHeaderValue(TrameGrpc.TypeDeContenu);
        requete.Headers.TE.ParseAdd("trailers");
        if (delai is { } d)
            requete.Headers.Add("grpc-timeout", TrameGrpc.FormaterDelai(d));   // le serveur arrête le travail à l'échéance

        using var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, annulation);
        if (reponse.StatusCode != HttpStatusCode.OK)
            throw new ErreurGrpc(StatutGrpc.Unavailable, $"Réponse HTTP {(int)reponse.StatusCode} : ce n'est pas un serveur gRPC.");

        // « Trailers-only » : une erreur immédiate peut arriver dans les en-têtes, sans corps.
        VerifierStatut(reponse.Headers);

        await using var flux = await reponse.Content.ReadAsStreamAsync(annulation);
        while (await LireTrame(flux, annulation) is { } message)
            yield return Protobuf.Protobuf.Deserialiser<TReponse>(message);

        if (!VerifierStatut(reponse.TrailingHeaders))
            throw new ErreurGrpc(StatutGrpc.Internal, "Statut gRPC absent des trailers.");
    }

    /// <summary>Une coupure en cours de flux : annulation si le client l'a voulue, sinon serveur indisponible.</summary>
    private static async Task<byte[]?> LireTrame(Stream flux, CancellationToken annulation)
    {
        try
        {
            return await TrameGrpc.Lire(flux, annulation);
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            annulation.ThrowIfCancellationRequested();
            throw new ErreurGrpc(StatutGrpc.Unavailable, $"Connexion interrompue : {e.Message}");
        }
    }

    private static bool VerifierStatut(HttpHeaders enTetes)
    {
        if (!enTetes.TryGetValues("grpc-status", out var valeurs))
            return false;
        var statut = (StatutGrpc)int.Parse(valeurs.First());
        if (statut == StatutGrpc.Ok)
            return true;
        var message = enTetes.TryGetValues("grpc-message", out var m) ? Uri.UnescapeDataString(m.First()) : "";
        throw new ErreurGrpc(statut, message);
    }
}

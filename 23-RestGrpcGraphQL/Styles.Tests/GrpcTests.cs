using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Styles.Contrats;
using Styles.Outillage.Grpc;
using Styles.Outillage.Protobuf;

namespace Styles.Tests;

public class GrpcTests : IAsyncLifetime
{
    private readonly Systeme _systeme = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _systeme.DisposeAsync();

    /// <summary>Un appel gRPC « à la main » : de quoi voir ce qui circule vraiment.</summary>
    private async Task<HttpResponseMessage> AppelBrut(string methode, object demande, string typeDeContenu = TrameGrpc.TypeDeContenu)
    {
        var requete = new HttpRequestMessage(HttpMethod.Post, $"/{ServiceBanque.Nom}/{methode}")
        {
            Version = HttpVersion.Version20,
            Content = new ByteArrayContent(TrameGrpc.Encadrer(Protobuf.Serialiser(demande)))
        };
        requete.Content.Headers.ContentType = new MediaTypeHeaderValue(typeDeContenu);
        var reponse = await _systeme.Http.SendAsync(requete);
        await reponse.Content.LoadIntoBufferAsync();   // les trailers ne sont connus qu'une fois le corps lu
        return reponse;
    }

    private static string Trailer(HttpResponseMessage reponse, string nom) => reponse.TrailingHeaders.GetValues(nom).Single();

    [Fact]
    public async Task Un_appel_unaire_est_un_POST_dont_le_corps_est_un_message_encadre_et_le_statut_dans_les_trailers()
    {
        var reponse = await AppelBrut("ObtenirCompte", new DemandeCompte { Numero = "10002" });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("application/grpc", reponse.Content.Headers.ContentType!.MediaType);
        var corps = await reponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(0, corps[0]);   // non compressé
        Assert.Equal(corps.Length - 5, (corps[1] << 24) | (corps[2] << 16) | (corps[3] << 8) | corps[4]);
        var compte = Protobuf.Deserialiser<Compte>(corps.AsSpan(5));
        Assert.Equal(("Livret A", 815_000L, TypeCompte.Epargne), (compte.Libelle, compte.SoldeCentimes, compte.Type));
        Assert.Equal("0", Trailer(reponse, "grpc-status"));
    }

    [Fact]
    public async Task Une_erreur_metier_repond_quand_meme_200_avec_le_statut_gRPC_dans_les_trailers()
    {
        var reponse = await AppelBrut("ObtenirCompte", new DemandeCompte { Numero = "99999" });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Empty(await reponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(((int)StatutGrpc.NotFound).ToString(), Trailer(reponse, "grpc-status"));
        Assert.Equal("Compte « 99999 » inconnu.", Uri.UnescapeDataString(Trailer(reponse, "grpc-message")));

        var erreur = await Assert.ThrowsAsync<ErreurGrpc>(() =>
            _systeme.Grpc.Grpc.Unaire<DemandeCompte, Compte>("ObtenirCompte", new DemandeCompte { Numero = "99999" }));
        Assert.Equal(StatutGrpc.NotFound, erreur.Statut);
    }

    [Fact]
    public async Task Sans_le_type_de_contenu_gRPC_la_requete_est_refusee()
    {
        var reponse = await AppelBrut("ObtenirCompte", new DemandeCompte { Numero = "10001" }, "application/json");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_flux_pousse_chaque_operation_des_qu_elle_a_lieu()
    {
        using var arret = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var flux = _systeme.Grpc.SuivreOperations("10001", annulation: arret.Token).GetAsyncEnumerator(arret.Token);
        var premiere = flux.MoveNextAsync().AsTask();
        await AttendreQue(() => _systeme.Banque.NombreAbonnes == 1);
        Assert.False(premiere.IsCompleted);   // rien tant qu'il ne se passe rien

        await _systeme.Rest.Virer("20001", "10001", 12.34m, "Remboursement");

        Assert.True(await premiere);
        Assert.Equal(("Remboursement de 20001", 1234L), (flux.Current.Libelle, flux.Current.MontantCentimes));

        await _systeme.GraphQL.Virer("10001", "10002", 5m, "Épargne");
        Assert.True(await flux.MoveNextAsync());
        Assert.Equal(-500L, flux.Current.MontantCentimes);
    }

    [Fact]
    public async Task L_echeance_du_client_arrete_aussi_le_travail_cote_serveur()
    {
        using var gardeFou = new CancellationTokenSource(TimeSpan.FromSeconds(5));   // si l'échéance n'était pas tenue
        var chrono = Stopwatch.StartNew();
        var erreur = await Assert.ThrowsAsync<ErreurGrpc>(async () =>
        {
            await foreach (var _ in _systeme.Grpc.SuivreOperations("10001", TimeSpan.FromMilliseconds(300), gardeFou.Token)) { }
        });

        Assert.Equal(StatutGrpc.DeadlineExceeded, erreur.Statut);
        Assert.InRange(chrono.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
        await AttendreQue(() => _systeme.Banque.NombreAbonnes == 0);   // l'abonnement a été libéré
    }

    [Fact]
    public async Task Un_client_qui_abandonne_libere_le_serveur()
    {
        using var arret = new CancellationTokenSource();
        var ecoute = Task.Run(async () =>
        {
            await foreach (var _ in _systeme.Grpc.SuivreOperations("10001", annulation: arret.Token)) { }
        });
        await AttendreQue(() => _systeme.Banque.NombreAbonnes == 1);

        arret.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ecoute);
        await AttendreQue(() => _systeme.Banque.NombreAbonnes == 0);
    }

    [Fact]
    public async Task Suivre_un_compte_inconnu_echoue_tout_de_suite()
    {
        var erreur = await Assert.ThrowsAsync<ErreurGrpc>(async () =>
        {
            await foreach (var _ in _systeme.Grpc.SuivreOperations("99999", delai: TimeSpan.FromSeconds(5))) { }
        });
        Assert.Equal(StatutGrpc.NotFound, erreur.Statut);
    }

    private static async Task AttendreQue(Func<bool> condition)
    {
        var chrono = Stopwatch.StartNew();
        while (!condition())
        {
            if (chrono.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Condition non atteinte.");
            await Task.Delay(10);
        }
    }
}

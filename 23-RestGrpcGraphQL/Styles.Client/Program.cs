using System.Net;
using Styles.Client;
using Styles.Outillage.Grpc;

// Le client de comparaison : les mêmes usages, joués avec les trois API du serveur (lancé à part).
//   dotnet run --project Styles.Client [-- --latence 50]   (latence simulée par requête, en ms : un réseau mobile)

Console.OutputEncoding = System.Text.Encoding.UTF8;

var latence = TimeSpan.FromMilliseconds(args.SkipWhile(a => a != "--latence").Skip(1).Select(int.Parse).FirstOrDefault(50));

var compteurRest = new CompteurHttp { Latence = latence };
var compteurGraphQL = new CompteurHttp { Latence = latence };
var compteurGrpc = new CompteurHttp { Latence = latence };
var urlRest = new Uri("http://localhost:5123");
var clients = new (IClientBanque Client, CompteurHttp Compteur)[]
{
    (new ClientRest(new HttpClient(compteurRest) { BaseAddress = urlRest }), compteurRest),
    (new ClientGraphQL(new HttpClient(compteurGraphQL) { BaseAddress = urlRest }), compteurGraphQL),
    (new ClientGrpcBanque(new HttpClient(compteurGrpc) { BaseAddress = new Uri("http://localhost:5250") }), compteurGrpc),
};

try
{
    await new HttpClient().GetAsync(urlRest);
}
catch (HttpRequestException)
{
    Console.WriteLine("Le serveur ne répond pas : lancez d'abord  dotnet run --project Styles.Serveur");
    return 1;
}

Console.WriteLine($"═══ 1. L'écran d'accueil d'Alice (3 comptes, 3 dernières opérations chacun) — latence simulée {latence.TotalMilliseconds} ms/requête ═══\n");
foreach (var (client, _) in clients)
    await client.EcranAccueil("bob");   // tour de chauffe : connexions ouvertes, code compilé, avant de mesurer
EcranAccueil? reference = null;
foreach (var (client, compteur) in clients)
{
    var (ecran, mesure) = await Chrono.Mesurer(compteur, () => client.EcranAccueil("alice"));
    Console.WriteLine($"  {client.Nom,-8} {mesure}");
    reference ??= ecran;
    if (!Identiques(reference, ecran))
        Console.WriteLine("           ⚠ écran différent de celui obtenu en REST !");
}
Console.WriteLine();
foreach (var compte in reference!.Comptes)
{
    Console.WriteLine($"  {compte.Libelle,-15} {compte.Solde,10:N2} €");
    foreach (var operation in compte.Operations)
        Console.WriteLine($"      {operation.Date:dd/MM}  {operation.Libelle,-22} {operation.Montant,10:N2} €");
}

Console.WriteLine("\n═══ 2. Un virement refusé (solde insuffisant) : la même erreur, trois langues ═══\n");
foreach (var (client, _) in clients)
{
    var resultat = await client.Virer("20001", "10001", 1_000_000m, "Essai");
    Console.WriteLine($"  {client.Nom,-8} {resultat.Erreur,-22} « {resultat.Message} »");
}

Console.WriteLine("\n═══ 3. Suivre les opérations d'un compte en direct ═══\n");
var grpc = (ClientGrpcBanque)clients[2].Client;
var rest = new HttpClient(compteurRest) { BaseAddress = urlRest };
using (var arret = new CancellationTokenSource())
{
    var recues = new List<string>();
    var ecoute = Task.Run(async () =>
    {
        try
        {
            // Une seule requête, qui reste ouverte : le serveur pousse chaque opération. L'échéance la ferme.
            await foreach (var operation in grpc.SuivreOperations("10001", TimeSpan.FromSeconds(2), arret.Token))
                recues.Add($"{operation.Libelle} ({operation.MontantCentimes / 100m:N2} €)");
        }
        catch (ErreurGrpc e)
        {
            recues.Add($"fin du flux : {e.Statut}");
        }
    });
    await Task.Delay(300);
    foreach (var (client, _) in clients)
        await client.Virer("10002", "10001", 10m, $"Épargne via {client.Nom}");
    await ecoute;
    Console.WriteLine("  gRPC (flux serveur, 1 requête ouverte 2 s) :");
    foreach (var ligne in recues)
        Console.WriteLine($"      {ligne}");
}

// REST n'a pas de flux : on interroge. L'ETag rend l'interrogation peu coûteuse quand rien ne change.
string? etag = null;
int modifie = 0, inchange = 0;
var (_, mesurePolling) = await Chrono.Mesurer(compteurRest, async () =>
{
    for (var i = 0; i < 5; i++)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, "/comptes/10001");
        if (etag is not null)
            requete.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var reponse = await rest.SendAsync(requete);
        await reponse.Content.ReadAsByteArrayAsync();
        if (reponse.StatusCode == HttpStatusCode.NotModified)
            inchange++;
        else
        {
            modifie++;
            etag = reponse.Headers.ETag?.Tag;
        }
    }
    return 0;
});
Console.WriteLine($"\n  REST (interrogation avec ETag, 5 fois) : {modifie} × 200, {inchange} × 304 Not Modified — {mesurePolling}");

return 0;

static bool Identiques(EcranAccueil a, EcranAccueil b) =>
    a.Nom == b.Nom && a.Comptes.Count == b.Comptes.Count && a.Comptes.Zip(b.Comptes).All(p =>
        p.First.Numero == p.Second.Numero && p.First.Libelle == p.Second.Libelle && p.First.Solde == p.Second.Solde
        && p.First.Operations.SequenceEqual(p.Second.Operations));

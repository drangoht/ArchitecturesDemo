namespace Cell.Routeur;

public static class DonneesDeDemo
{
    /// <summary>Placés dans cet ordre, ils se répartissent : alice et david en c1, bob et emma en c2, carla en c3.</summary>
    public static readonly ClientExport[] Clients =
    [
        Client("alice", "Alice", "11111111-1111-1111-1111-111111111111", 1_000m),
        Client("bob", "Bob", "22222222-2222-2222-2222-222222222222", 500m),
        Client("carla", "Carla", "33333333-3333-3333-3333-333333333333", 800m),
        Client("david", "David", "55555555-5555-5555-5555-555555555555", 300m),
        Client("emma", "Emma", "66666666-6666-6666-6666-666666666666", 1_200m)
    ];

    private static ClientExport Client(string id, string nom, string compte, decimal solde) =>
        new(id, nom, [new CompteExport(Guid.Parse(compte), "Compte courant", solde)], [], []);
}

/// <summary>
/// Au démarrage, inscrit les clients de démo par le même chemin qu'un nouveau client (le plan de contrôle choisit la cellule).
/// Les cellules peuvent démarrer après le routeur : on réessaie jusqu'à ce qu'elles répondent.
/// </summary>
public sealed class AmorcageDemo(PlanDeControle plan, IConfiguration configuration, ILogger<AmorcageDemo> journal) : IHostedService
{
    public async Task StartAsync(CancellationToken arret)
    {
        if (!configuration.GetValue("Routeur:AmorcerDemo", true))
            return;
        var limite = DateTime.UtcNow.AddSeconds(30);
        foreach (var client in DonneesDeDemo.Clients)
        {
            while (true)
            {
                try
                {
                    var cellule = await plan.CreerClient(client);
                    journal.LogInformation("Client {Client} placé dans la cellule {Cellule}", client.Id, cellule.Id);
                    break;
                }
                catch (Exception e) when (e is HttpRequestException or PlanException { Statut: 502 } && DateTime.UtcNow < limite)
                {
                    await Task.Delay(500, arret);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken arret) => Task.CompletedTask;
}

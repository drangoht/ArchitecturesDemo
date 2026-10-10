using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Modulaire.Comptes;
using Modulaire.Comptes.Contrats;
using Modulaire.Partage;
using Modulaire.Virements.Contrats;

namespace Modulaire.Tests;

// Les modules ne se connaissent que par contrats : on peut en remplacer un par un faux.
public class IsolationTests
{
    [Fact]
    public async Task Le_module_Virements_compense_si_le_credit_echoue()
    {
        var faux = new FauxComptes();
        await using var app = new BanqueApplication(services => services.AddScoped<IComptesApi>(_ => faux));

        var reponse = await app.CreateClient().PostAsJsonAsync("/virements",
            new { source = FauxComptes.Source, cible = FauxComptes.Cible, montant = 40m });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(["débit source 40", "crédit cible 40 (refusé)", "crédit source 40"], faux.Journal);
    }

    [Fact]
    public async Task Une_panne_chez_un_abonne_ne_casse_pas_le_module_qui_publie()
    {
        await using var app = new BanqueApplication(services =>
            services.AddScoped<IGestionnaireEvenement<VirementEffectue>, GestionnaireEnPanne>());
        var client = app.CreateClient();

        var reponse = await client.PostAsJsonAsync("/virements",
            new { source = ComptesModule.Alice, cible = ComptesModule.Bob, montant = 10m });

        reponse.EnsureSuccessStatusCode();
        // Le gestionnaire de Notifications a quand même fait son travail.
        var notifications = await client.GetFromJsonAsync<JsonElement>($"/notifications/{ComptesModule.Bob}");
        Assert.Equal(1, notifications.GetArrayLength());
    }

    private class GestionnaireEnPanne : IGestionnaireEvenement<VirementEffectue>
    {
        public Task Traiter(VirementEffectue evenement) => throw new InvalidOperationException("Panne simulée");
    }

    private class FauxComptes : IComptesApi
    {
        public static readonly Guid Source = Guid.NewGuid();
        public static readonly Guid Cible = Guid.NewGuid();

        public List<string> Journal { get; } = [];

        public Task<CompteInfo?> Consulter(Guid compteId) =>
            Task.FromResult<CompteInfo?>(new CompteInfo(compteId, Nom(compteId), 100m));

        public Task<ResultatOperation> Debiter(Guid compteId, decimal montant)
        {
            Journal.Add($"débit {Nom(compteId)} {montant}");
            return Task.FromResult(ResultatOperation.Ok());
        }

        public Task<ResultatOperation> Crediter(Guid compteId, decimal montant)
        {
            if (compteId == Cible)
            {
                Journal.Add($"crédit cible {montant} (refusé)");
                return Task.FromResult(ResultatOperation.Refuse("Compte cible bloqué."));
            }
            Journal.Add($"crédit {Nom(compteId)} {montant}");
            return Task.FromResult(ResultatOperation.Ok());
        }

        private static string Nom(Guid id) => id == Source ? "source" : "cible";
    }
}

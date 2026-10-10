using Cqrs.Evenements;
using Cqrs.Lecture;
using Cqrs.Lecture.Projections;
using Cqrs.Lecture.Requetes;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Tests;

/// <summary>Le côté lecture se teste sans le côté écriture : on lui donne des événements, on interroge ses vues.</summary>
public class LectureTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly EvenementEnregistre[] Historique =
    [
        new(1, new CompteOuvert(Alice, "Alice", 1000m, T0)),
        new(2, new CompteOuvert(Bob, "Bob", 500m, T0.AddMinutes(1))),
        new(3, new ArgentRetire(Alice, 100m, 900m, T0.AddMinutes(2))),
        new(4, new VirementEffectue(Guid.NewGuid(), Alice, Bob, 250m, 650m, 750m, T0.AddMinutes(3)))
    ];

    private static async Task<IServiceProvider> ProjeterHistorique()
    {
        var nomBase = Guid.NewGuid().ToString();
        var services = new ServiceCollection().AddCoteLecture(_ => nomBase).BuildServiceProvider();
        using var scope = services.CreateScope();
        var projecteur = scope.ServiceProvider.GetRequiredService<Projecteur>();
        foreach (var evenement in Historique)
            await projecteur.Appliquer(evenement);
        return services;
    }

    private static async Task<T> Demander<T>(IServiceProvider services, IRequete<T> requete)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BusRequetes>().Demander(requete);
    }

    [Fact]
    public async Task Le_releve_est_pret_a_afficher()
    {
        var services = await ProjeterHistorique();

        var releve = await Demander(services, new ConsulterReleve(Alice));

        Assert.Equal(650m, releve!.Solde);
        Assert.Equal(3, releve.NombreOperations);
        Assert.Equal(["Virement vers Bob", "Retrait", "Ouverture du compte"], releve.DernieresOperations.Select(o => o.Libelle));
    }

    [Fact]
    public async Task L_historique_contient_des_libelles_deja_rediges()
    {
        var services = await ProjeterHistorique();

        var operations = await Demander(services, new ListerOperations(Bob, Page: 1, Taille: 10));

        Assert.Equal(new OperationVue("Virement reçu de Alice", 250m, 750m, T0.AddMinutes(3)), operations[0]);
    }

    [Fact]
    public async Task Le_tableau_de_bord_est_maintenu_au_fil_des_evenements()
    {
        var services = await ProjeterHistorique();

        var tableau = await Demander(services, new ConsulterTableauDeBord());

        Assert.Equal(new TableauDeBordVue(NombreComptes: 2, EncoursTotal: 1400m, NombreVirements: 1, MontantTotalVire: 250m), tableau);
    }

    [Fact]
    public async Task Rejouer_un_evenement_deja_projete_n_a_aucun_effet()
    {
        var services = await ProjeterHistorique();

        using (var scope = services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Projecteur>().Appliquer(Historique[2]);  // le retrait, encore

        Assert.Equal(650m, (await Demander(services, new ConsulterReleve(Alice)))!.Solde);
        Assert.Equal(1400m, (await Demander(services, new ConsulterTableauDeBord())).EncoursTotal);
    }
}

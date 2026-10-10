using System.Text.Json;
using Sls.Fonctions;
using Sls.Plateforme;

namespace Sls.Tests;

/// <summary>Une fonction serverless est une simple méthode : on l'appelle directement, avec des services gérés en mémoire.</summary>
public class FonctionsTests
{
    private readonly ServicesGeres _services = new(new OptionsPlateforme());
    private ContexteFonction Contexte => ContexteFonction.PourTests(_services);

    public FonctionsTests() => DonneesDeDemo.Charger(_services);

    private static RequeteHttp Requete(object corps, string id = "") =>
        new("POST", "/", new Dictionary<string, string> { ["id"] = id }, JsonSerializer.Serialize(corps));

    private async Task<decimal> Solde(Guid id) => (await _services.Table(Tables.Comptes).Lire<Compte>(id.ToString())).Valeur!.Solde;

    [Fact]
    public async Task Des_retraits_simultanes_ne_mettent_jamais_le_compte_a_decouvert()
    {
        // 30 « instances » retirent 100 € en même temps sur un compte de 1 000 € : grâce aux ETag, exactement 10 réussissent.
        var reponses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
            ComptesFonctions.Retirer(Requete(new { montant = 100m }, DonneesDeDemo.Alice.ToString()), Contexte)));

        Assert.Equal(10, reponses.Count(r => r.Statut == 200));
        Assert.Equal(0m, await Solde(DonneesDeDemo.Alice));
    }

    [Fact]
    public async Task Un_message_de_virement_livre_deux_fois_ne_debite_qu_une_fois()
    {
        var virement = await VirementsFonctions.Planifier(
            new VirementsFonctions.VirementDemande(DonneesDeDemo.Alice, DonneesDeDemo.Bob, 100m), Contexte, Guid.NewGuid());
        var message = new MessageFile("m1", Files.Virements, JsonSerializer.Serialize(new { virementId = virement.Id }), 1);

        await VirementsFonctions.Executer(message, Contexte);
        await VirementsFonctions.Executer(message, Contexte);  // double livraison

        Assert.Equal(900m, await Solde(DonneesDeDemo.Alice));
        Assert.Equal(600m, await Solde(DonneesDeDemo.Bob));
    }

    [Fact]
    public async Task Un_refus_metier_est_definitif_et_ne_provoque_pas_de_nouvel_essai()
    {
        var virement = await VirementsFonctions.Planifier(
            new VirementsFonctions.VirementDemande(DonneesDeDemo.Bob, DonneesDeDemo.Alice, 9_999m), Contexte, Guid.NewGuid());

        // Pas d'exception : la plateforme considère le message comme traité.
        await VirementsFonctions.Executer(
            new MessageFile("m1", Files.Virements, JsonSerializer.Serialize(new { virementId = virement.Id }), 1), Contexte);

        var (resultat, _) = await _services.Table(Tables.Virements).Lire<Virement>(virement.Id.ToString());
        Assert.Equal(("Refuse", "Solde insuffisant."), (resultat!.Statut, resultat.Erreur));
    }

    [Fact]
    public async Task Retraiter_le_meme_fichier_d_import_ne_cree_pas_de_doublons()
    {
        var fichier = new ObjetStocke("imports/test.csv",
            $"source;cible;montant\n{DonneesDeDemo.Carla};{DonneesDeDemo.Alice};10\n{DonneesDeDemo.Carla};{DonneesDeDemo.Bob};20\n");

        await ImportsFonctions.Traiter(fichier, Contexte);
        await ImportsFonctions.Traiter(fichier, Contexte);  // nouvel essai du déclencheur de stockage

        Assert.Equal(2, (await _services.Table(Tables.Virements).Lister<Virement>()).Count);
    }

    [Fact]
    public async Task La_memoire_d_instance_n_est_pas_partagee_contrairement_au_service_gere()
    {
        var (instanceA, instanceB) = (new Dictionary<string, object>(), new Dictionary<string, object>());
        var requete = new RequeteHttp("GET", "/visites", new Dictionary<string, string>(), "");

        await VisitesFonctions.Compter(requete, ContexteFonction.PourTests(_services, instanceA));
        await VisitesFonctions.Compter(requete, ContexteFonction.PourTests(_services, instanceA));
        var reponse = await VisitesFonctions.Compter(requete, ContexteFonction.PourTests(_services, instanceB));

        var corps = JsonSerializer.SerializeToElement(reponse.Corps);
        Assert.Equal(1, corps.GetProperty("compteurDeLInstance").GetInt64());  // l'instance B ne sait rien des 2 premiers appels
        Assert.Equal(3, corps.GetProperty("compteurGlobal").GetInt64());
    }
}

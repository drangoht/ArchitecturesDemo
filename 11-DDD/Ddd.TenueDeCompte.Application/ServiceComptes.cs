using Ddd.TenueDeCompte.Contrats;
using Ddd.TenueDeCompte.Domaine;
using Ddd.TenueDeCompte.Domaine.Comptes;
using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Application;

public interface IUniteDeTravail
{
    Task Valider();
}

public record MouvementVue(DateTime Date, string Type, string Libelle, string Montant);

public record CompteVue(
    Guid Id, string Iban, string Titulaire, decimal Solde, string Devise, string SoldeAffiche,
    string PlafondRetraitJournalier, string Statut, string? MotifBlocage, IReadOnlyList<MouvementVue> DerniersMouvements);

/// <summary>
/// Service d'application : il traduit les données brutes (string, decimal) en objets du domaine,
/// orchestre, puis publie les événements une fois les changements enregistrés.
/// </summary>
public class ServiceComptes(
    IComptes comptes,
    FabriqueDeComptes fabrique,
    ServiceDeVirement serviceDeVirement,
    IUniteDeTravail uniteDeTravail,
    PublicateurEvenements publicateur,
    TimeProvider horloge)
{
    private DateTime Maintenant => horloge.GetUtcNow().UtcDateTime;

    public async Task<CompteVue> Ouvrir(string titulaire, decimal depotInitial, string devise, Guid? id = null)
    {
        var compte = await fabrique.Ouvrir(
            NomTitulaire.Depuis(titulaire), new Argent(depotInitial, Devise.De(devise)), Maintenant,
            id is null ? null : new CompteId(id.Value));
        await comptes.Ajouter(compte);
        await Terminer(compte);
        return Vue(compte);
    }

    public async Task<CompteVue> Consulter(Guid id) => Vue(await Charger(id));

    public async Task<IReadOnlyList<CompteVue>> Lister() => (await comptes.Tous()).Select(Vue).ToList();

    public async Task<CompteVue> Retirer(Guid id, decimal montant)
    {
        var compte = await Charger(id);
        compte.Retirer(new Argent(montant, compte.Devise), Maintenant);
        await Terminer(compte);
        return Vue(compte);
    }

    public async Task<CompteVue> Virer(Guid sourceId, string ibanBeneficiaire, decimal montant)
    {
        var source = await Charger(sourceId);
        var iban = Iban.Depuis(ibanBeneficiaire);
        var cible = await comptes.ParIban(iban) ?? throw new CompteIntrouvableException($"aucun compte de la banque ne porte l'IBAN {iban}");

        await serviceDeVirement.Virer(source, cible, new Argent(montant, source.Devise), Maintenant);
        await Terminer(source, cible);
        return Vue(source);
    }

    public async Task Bloquer(Guid id, string motif)
    {
        var compte = await Charger(id);
        compte.Bloquer(motif, Maintenant);
        await Terminer(compte);
    }

    private async Task<Compte> Charger(Guid id) =>
        await comptes.ParId(new CompteId(id)) ?? throw new CompteIntrouvableException(id.ToString());

    /// <summary>Enregistre d'abord, publie ensuite : on n'annonce jamais un fait qui n'a pas été enregistré.</summary>
    private async Task Terminer(params Compte[] modifies)
    {
        await uniteDeTravail.Valider();
        var evenements = modifies.SelectMany(c => c.ExtraireEvenements()).ToList();
        await publicateur.Publier(evenements);
    }

    private static CompteVue Vue(Compte c) => new(
        c.Id.Valeur, c.Iban.ToString(), c.Titulaire.Valeur, c.Solde.Montant, c.Devise.Code, c.Solde.ToString(),
        c.PlafondRetraitJournalier.ToString(), c.Statut.ToString(), c.MotifBlocage,
        c.Mouvements.OrderByDescending(m => m.Date).Take(10)
            .Select(m => new MouvementVue(m.Date, m.Type.ToString(), m.Libelle, m.Montant.ToString())).ToList());
}

using Ddd.TenueDeCompte.Contrats;
using Ddd.TenueDeCompte.Domaine;
using Ddd.TenueDeCompte.Domaine.Comptes;
using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Application;

/// <summary>
/// Traduit les événements de DOMAINE (internes, riches) en événements d'INTÉGRATION (langage publié, simple),
/// puis prévient les autres contextes. Le modèle interne ne fuit jamais au-dehors.
/// </summary>
/// <param name="abonnes">Résolus au moment de la publication (et non à la construction) pour éviter un cycle de dépendances.</param>
public class PublicateurEvenements(Func<IEnumerable<IAbonneTenueDeCompte>> abonnes, IConvertisseurDevises convertisseur)
{
    public async Task Publier(IEnumerable<IEvenementDomaine> evenements)
    {
        foreach (var evenement in evenements)
        {
            if (evenement is not VirementEffectue v)
                continue;

            var enEuros = v.MontantDebite.Devise == Devise.Euro
                ? v.MontantDebite
                : await convertisseur.Convertir(v.MontantDebite, Devise.Euro);

            var integration = new VirementEmis(
                v.VirementId, v.Source.Valeur, v.IbanSource.Valeur, v.TitulaireSource.Valeur, v.IbanCible.Valeur,
                v.MontantDebite.Montant, v.MontantDebite.Devise.Code, enEuros.Montant, v.Date);

            foreach (var abonne in abonnes())
                await abonne.SurVirementEmis(integration);
        }
    }
}

/// <summary>Implémentation du service hôte ouvert : ce que les autres contextes peuvent demander.</summary>
public class TenueDeCompteApi(ServiceComptes comptes) : ITenueDeCompteApi
{
    public Task BloquerCompte(Guid compteId, string motif) => comptes.Bloquer(compteId, motif);
}

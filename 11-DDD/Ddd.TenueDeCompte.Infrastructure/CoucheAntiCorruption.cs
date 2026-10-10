using System.Globalization;
using Ddd.TenueDeCompte.Domaine;
using Ddd.TenueDeCompte.Domaine.Comptes;
using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Infrastructure;

/// <summary>
/// Système externe « historique » (simulé), qu'on ne peut pas modifier. Son modèle est très différent du nôtre :
/// une chaîne de cotations « EURUSD=10832;… », taux multipliés par 10 000, uniquement à partir de l'euro,
/// et des devises que notre banque ne tient pas.
/// </summary>
public class SystemeDeCotationsHerite
{
    public virtual string CotationsDuJour() => "EURUSD=10832;EURCHF=9415;EURGBP=8571;EURJPY=1612300";
}

/// <summary>
/// COUCHE ANTI-CORRUPTION : elle traduit le modèle du système hérité vers notre langage (Argent, Devise).
/// Sans elle, les « points de base » et les paires « EURxxx » finiraient par se répandre dans le domaine.
/// Si le fournisseur change, seule cette classe est réécrite.
/// </summary>
public class AdaptateurTauxDeChange(SystemeDeCotationsHerite systemeHerite) : IConvertisseurDevises
{
    public Task<Argent> Convertir(Argent montant, Devise devise)
    {
        var tauxDepuisEuro = LireCotations(systemeHerite.CotationsDuJour());

        // Le système hérité ne cote que depuis l'euro : on passe par l'euro pour tout croisement (USD → CHF…).
        var montantEnEuros = montant.Montant / tauxDepuisEuro[montant.Devise];
        var converti = decimal.Round(montantEnEuros * tauxDepuisEuro[devise], 2, MidpointRounding.ToEven);
        return Task.FromResult(new Argent(converti, devise));
    }

    private static Dictionary<Devise, decimal> LireCotations(string cotations)
    {
        var taux = new Dictionary<Devise, decimal> { [Devise.Euro] = 1m };
        foreach (var cotation in cotations.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (paire, valeur) = (cotation.Split('=')[0], cotation.Split('=')[1]);
            if (!paire.StartsWith("EUR"))
                continue;
            try
            {
                taux[Devise.De(paire[3..])] = decimal.Parse(valeur, CultureInfo.InvariantCulture) / 10_000m;
            }
            catch (RegleMetierException)
            {
                // Devise que la banque ne tient pas (GBP, JPY…) : elle ne pénètre pas dans notre modèle.
            }
        }
        return taux;
    }
}

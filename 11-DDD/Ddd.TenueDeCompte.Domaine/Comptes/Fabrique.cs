using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Domaine.Comptes;

/// <summary>
/// Dépôt (repository) : un par racine d'agrégat, avec le vocabulaire du domaine.
/// Il charge et enregistre des agrégats ENTIERS (compte + mouvements), jamais des morceaux.
/// </summary>
public interface IComptes
{
    Task<Compte?> ParId(CompteId id);
    Task<Compte?> ParIban(Iban iban);
    Task<IReadOnlyList<Compte>> Tous();
    Task Ajouter(Compte compte);
    Task<long> ProchainNumeroDeCompte();
}

/// <summary>
/// Fabrique : l'ouverture d'un compte demande plusieurs choses (numéro, calcul de l'IBAN, plafond selon la devise).
/// Cette logique de création n'appartient ni au compte lui-même ni au code applicatif.
/// </summary>
public class FabriqueDeComptes(IComptes comptes)
{
    public const string CodeBanque = "30001";
    public const string CodeGuichet = "00001";

    public static readonly Argent DepotMinimum = new(10m, Devise.Euro);

    public async Task<Compte> Ouvrir(NomTitulaire titulaire, Argent depotInitial, DateTime date, CompteId? id = null)
    {
        if (depotInitial.Montant < 10m)
            throw new RegleMetierException($"Le dépôt initial doit être d'au moins 10 {depotInitial.Devise}.");

        var numero = await comptes.ProchainNumeroDeCompte();
        var iban = Iban.Generer("FR", $"{CodeBanque}{CodeGuichet}{numero:D11}00");
        var plafond = new Argent(1_000m, depotInitial.Devise);

        return Compte.Ouvrir(id ?? CompteId.Nouveau(), iban, titulaire, depotInitial, plafond, date);
    }
}

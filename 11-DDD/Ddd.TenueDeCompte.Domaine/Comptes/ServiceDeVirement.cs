using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Domaine.Comptes;

/// <summary>
/// Port vers un service de change. Le domaine dit ce dont il a besoin, dans SON langage (Argent, Devise) ;
/// l'infrastructure fournit l'implémentation (ici via une couche anti-corruption).
/// </summary>
public interface IConvertisseurDevises
{
    Task<Argent> Convertir(Argent montant, Devise devise);
}

/// <summary>
/// Service de domaine : le virement implique DEUX agrégats et un taux de change.
/// Cette règle n'appartient naturellement à aucun des deux comptes : elle vit ici, sans état.
/// </summary>
public class ServiceDeVirement(IConvertisseurDevises convertisseur)
{
    public async Task Virer(Compte source, Compte cible, Argent montant, DateTime date)
    {
        if (source.Id == cible.Id)
            throw new RegleMetierException("Impossible de virer vers le même compte.");
        if (montant.Devise != source.Devise)
            throw new RegleMetierException($"Un virement s'exprime dans la devise du compte débité ({source.Devise}).");

        var montantCredite = cible.Devise == montant.Devise ? montant : await convertisseur.Convertir(montant, cible.Devise);

        source.Debiter(montant, $"Virement vers {cible.Titulaire}", date);
        cible.Crediter(montantCredite, $"Virement de {source.Titulaire}", date);

        source.Signaler(new VirementEffectue(Guid.NewGuid(), source.Id, source.Iban, source.Titulaire,
            cible.Iban, montant, montantCredite, date));
    }
}

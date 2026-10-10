using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Domaine;

public class RegleMetierException(string message) : Exception(message);

public class CompteIntrouvableException(string description) : Exception($"Compte introuvable : {description}.");

/// <summary>Événement de domaine : un fait métier, interne à ce contexte délimité, exprimé avec ses objets-valeurs.</summary>
public interface IEvenementDomaine
{
    DateTime Date { get; }
}

public record CompteOuvert(CompteId CompteId, Iban Iban, NomTitulaire Titulaire, Argent DepotInitial, DateTime Date) : IEvenementDomaine;

public record RetraitEffectue(CompteId CompteId, Argent Montant, DateTime Date) : IEvenementDomaine;

public record VirementEffectue(
    Guid VirementId, CompteId Source, Iban IbanSource, NomTitulaire TitulaireSource,
    Iban IbanCible, Argent MontantDebite, Argent MontantCredite, DateTime Date) : IEvenementDomaine;

public record CompteBloque(CompteId CompteId, string Motif, DateTime Date) : IEvenementDomaine;

/// <summary>Une racine d'agrégat accumule les événements de domaine qu'elle lève, jusqu'à leur publication.</summary>
public abstract class RacineAgregat
{
    private readonly List<IEvenementDomaine> _evenements = [];

    public IReadOnlyList<IEvenementDomaine> EvenementsDomaine => _evenements;

    protected void Lever(IEvenementDomaine evenement) => _evenements.Add(evenement);

    public IReadOnlyList<IEvenementDomaine> ExtraireEvenements()
    {
        var evenements = _evenements.ToList();
        _evenements.Clear();
        return evenements;
    }
}

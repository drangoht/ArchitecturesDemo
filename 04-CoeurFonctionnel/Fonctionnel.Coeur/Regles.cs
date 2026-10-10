using System.Diagnostics;

namespace Fonctionnel.Coeur;

public static class Regles
{
    public const decimal PlafondVirementJournalier = 2_000m;

    public static DecisionRetrait Retirer(Compte compte, decimal montant) => montant switch
    {
        <= 0 => new DecisionRetrait.Refuse("Le montant doit être strictement positif."),
        _ when montant > compte.Solde => new DecisionRetrait.Refuse("Solde insuffisant."),
        _ => new DecisionRetrait.Accepte(compte with { Solde = compte.Solde - montant }, montant)
    };

    /// <param name="dejaVireAujourdhui">
    /// Ce qui dépend du temps ou de la base est calculé par la coquille et passé en paramètre :
    /// le cœur n'appelle jamais DateTime.Now ni la base de données.
    /// </param>
    public static DecisionVirement Virer(Compte source, Compte cible, decimal montant, decimal dejaVireAujourdhui)
    {
        if (source.Id == cible.Id)
            return new DecisionVirement.Refuse("Impossible de virer vers le même compte.");

        if (dejaVireAujourdhui + montant > PlafondVirementJournalier)
            return new DecisionVirement.Refuse(
                $"Plafond journalier de {PlafondVirementJournalier:0} € dépassé (déjà viré aujourd'hui : {dejaVireAujourdhui:0.00} €).");

        // On réutilise la règle de retrait : des fonctions pures se composent naturellement.
        return Retirer(source, montant) switch
        {
            DecisionRetrait.Accepte retrait => new DecisionVirement.Accepte(
                retrait.CompteMisAJour,
                cible with { Solde = cible.Solde + montant },
                montant),
            DecisionRetrait.Refuse refus => new DecisionVirement.Refuse(refus.Raison),
            _ => throw new UnreachableException()
        };
    }
}

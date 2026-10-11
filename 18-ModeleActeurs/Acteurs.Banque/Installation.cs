using Acteurs.Noyau;

namespace Acteurs.Banque;

public static class Installation
{
    /// <summary>
    /// Déclare les types d'acteurs de la banque et leur supervision.
    /// Les dépendances (stockage, pièges, horloge) sont capturées par les fabriques : chaque incarnation repart d'un objet neuf.
    /// </summary>
    public static SystemeActeurs InstallerBanque(this SystemeActeurs systeme, MagasinEtats magasin, Pieges pieges,
        TimeProvider horloge, StrategieSupervision? strategie = null)
    {
        systeme.Enregistrer(CompteActeur.Type, () => new CompteActeur(magasin, pieges, horloge), strategie);
        systeme.Enregistrer(VirementActeur.Type, () => new VirementActeur(magasin, pieges), strategie);
        return systeme;
    }

    public static ReferenceActeur Compte(this SystemeActeurs systeme, Guid id) => systeme.Acteur(CompteActeur.Type, id);

    public static ReferenceActeur Virement(this SystemeActeurs systeme, string id) => systeme.Acteur(VirementActeur.Type, id);
}

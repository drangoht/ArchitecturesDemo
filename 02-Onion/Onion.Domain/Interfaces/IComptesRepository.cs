using Onion.Domain.Model;

namespace Onion.Domain.Interfaces;

// Les interfaces d'accès aux données sont déclarées au centre ;
// l'infrastructure (couche externe) les implémente : les dépendances pointent vers l'intérieur.

public interface IComptesRepository
{
    Task<Compte?> ParId(Guid id);
    Task Ajouter(Compte compte);
    Task<bool> EstVide();
}

/// <summary>Valide en une fois toutes les modifications faites pendant un cas d'utilisation.</summary>
public interface IUnitOfWork
{
    Task Enregistrer();
}

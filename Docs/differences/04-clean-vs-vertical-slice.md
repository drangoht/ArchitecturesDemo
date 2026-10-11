# Clean ou Vertical Slice ?

**En anglais** : *Clean Architecture or Vertical Slice Architecture?*

> **La différence en une phrase** : Clean découpe le code **par couche technique** (un cas d'utilisation traverse
> quatre cercles), Vertical Slice le découpe **par fonctionnalité** (un cas d'utilisation tient dans un fichier).

📁 Démos : [`03-Clean`](../../03-Clean) · [`05-VerticalSlice`](../../05-VerticalSlice) ·
📖 Pages : [Clean](../03-clean.md) · [Vertical Slice](../05-vertical-slice.md) · 🗂️ Fiches : [3](../fiches/03-clean.md) · [5](../fiches/05-vertical-slice.md)

⬅️ [Onion ou Clean ?](03-onion-vs-clean.md) · ➡️ [Hexagonale ou Cœur fonctionnel ?](05-hexagonale-vs-coeur-fonctionnel.md)

---

## Ce qu'elles partagent

Les deux font du **cas d'utilisation** l'unité de base du code : `RetraitInteracteur` d'un côté, `RetirerArgent` de
l'autre. On trouve facilement « ce qui se passe quand on retire de l'argent ». Elles divergent sur tout le reste.

## Les différences point par point

| | Clean | Vertical Slice |
|---|---|---|
| Axe de découpe | **horizontal** : entités, cas d'utilisation, adaptateurs, web | **vertical** : une tranche par requête ou commande |
| Ajouter « retirer de l'argent » | un fichier dans chaque cercle, 4 projets | **un fichier** + une ligne dans `Program.cs` |
| Abstractions | une interface par frontière (entrée, sortie, passerelle) | aucune « au cas où » : le `DbContext` est déjà dépôt + unité de travail |
| Couplage | faible partout | **fort dans** la tranche, **faible entre** les tranches |
| Lecture et écriture | même chemin (interacteur, passerelle) | chaque tranche choisit : entité pour écrire, **projection LINQ** pour lire |
| Règles partagées | dans les entités, d'emblée | **remontées** dans `Domain/` quand une deuxième tranche en a besoin |
| Changer de base | réécrire la passerelle | plus coûteux : le `DbContext` est dans chaque tranche |
| Risque | cérémonie, abstractions vides | duplication, handlers obèses |

## Côte à côte

```mermaid
flowchart LR
    subgraph C["CLEAN : un retrait traverse 4 projets"]
        direction TB
        W["Clean.Web<br/>route"] --> A["AdaptateursInterface<br/>contrôleur, presenter"] --> U["CasUtilisation<br/>interacteur"] --> E["Entites<br/>Compte"]
    end
    subgraph V["VERTICAL SLICE : un retrait = 1 fichier"]
        direction TB
        R["RetirerArgent.cs<br/>route, validation,<br/>règle, accès BD"] --> D["Domain/Compte<br/>règle partagée"]
    end
    C ~~~ V
```

## Dans le code des démos

**Vertical Slice** : la tranche complète, de la route à la base
([RetirerArgent.cs](../../05-VerticalSlice/Vertical.Api/Features/Comptes/RetirerArgent.cs)) :

```csharp
public static void Map(IEndpointRouteBuilder app) => app.MapPost("/comptes/{id:guid}/retrait", Executer);

private static async Task<IResult> Executer(Guid id, Requete requete, BanqueDbContext db)
{
    // … validation du montant …
    var compte = await db.Comptes.FindAsync(id);
    if (compte is null)
        return Results.NotFound();

    if (compte.Retirer(requete.Montant) is { } refus)
        return Results.BadRequest(new { erreur = refus });

    db.Operations.Add(new Operation { CompteId = id, Libelle = "Retrait", Montant = -requete.Montant });
    await db.SaveChangesAsync();

    return Results.Ok(new Reponse(id, compte.Solde));
}
```

**Clean** : le même retrait réparti entre la route ([Program.cs](../../03-Clean/Clean.Web/Program.cs)), le contrôleur,
l'interacteur ([Retrait.cs](../../03-Clean/Clean.CasUtilisation/Retrait/Retrait.cs)), le presenter
([PresentersHttp.cs](../../03-Clean/Clean.AdaptateursInterface/Presenters/PresentersHttp.cs)) et la passerelle :

```csharp
public class RetraitInteracteur(IComptesPasserelle comptes) : IRetraitEntree
{
    public async Task Executer(DemandeRetrait demande, IRetraitSortie sortie)
    {
        var compte = await comptes.Charger(demande.CompteId);
        // … Refus, CompteIntrouvable ou Succes, poussés vers le presenter …
    }
}
```

**La lecture** montre le mieux l'écart. Vertical Slice projette directement vers la réponse, en lecture seule, sans
passer par le domaine ([ConsulterCompte.cs](../../05-VerticalSlice/Vertical.Api/Features/Comptes/ConsulterCompte.cs)) :

```csharp
var compte = await db.Comptes
    .AsNoTracking()
    .Where(c => c.Id == id)
    .Select(c => new { c.Id, c.Titulaire, c.Solde })
    .FirstOrDefaultAsync();
```

En Clean, une consultation passe par le même chemin qu'une écriture : interacteur, passerelle, entité, presenter.

## Comment choisir

- **Vertical Slice** pour une API de gestion livrée fonctionnalité par fonctionnalité, où les fonctionnalités
  évoluent indépendamment et où la base ne changera pas.
- **Clean** (ou Onion) quand l'infrastructure doit rester remplaçable, que les mêmes règles servent plusieurs canaux,
  ou que le domaine est riche et partagé par toutes les fonctionnalités.

## Les combiner

Courant et efficace : des **tranches verticales dans chaque module** d'un
[monolithe modulaire](09-monolithe-modulaire-vs-microservices.md), un domaine riche pour les règles partagées, et des
ports hexagonaux **seulement autour des services externes** (paiement, courriel), là où le remplacement est probable.

## Idées reçues

- *« Vertical Slice = pas d'architecture »* : c'est une architecture avec des règles (une tranche par requête,
  couplage fort dedans, faible entre), qui demande la discipline de reconnaître et remonter le code partagé.
- *« Vertical Slice impose MediatR »* : non, la démo s'en passe ; une méthode statique par tranche suffit
  (MediatR est d'ailleurs payant pour les entreprises depuis 2025).

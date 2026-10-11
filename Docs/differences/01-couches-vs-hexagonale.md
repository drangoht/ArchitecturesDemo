# Couches ou Hexagonale ?

**En anglais** : *Layered Architecture or Hexagonal Architecture?*

> **La différence en une phrase** : dans les couches, le métier **dépend de la base** ; dans l'hexagonale, c'est
> l'inverse, la base **se branche sur le métier**. Tout le reste en découle.

📁 Démos : [`00-Couches`](../../00-Couches) · [`01-Hexagonale`](../../01-Hexagonale) · · 🌐 [English](../en/differences/01-couches-vs-hexagonale.md)
📖 Pages : [Couches](../00-couches.md) · [Hexagonale](../01-hexagonale.md) · 🗂️ Fiches : [0](../fiches/00-couches.md) · [1](../fiches/01-hexagonale.md)

⬅️ [Sommaire](README.md) · ➡️ [Hexagonale ou Onion ?](02-hexagonale-vs-onion.md)

---

## Ce qu'elles partagent

Les deux séparent la technique (HTTP, base) du métier, et toutes deux tiennent dans **un seul déploiement**. Le
métier a les mêmes règles (montant positif, solde suffisant), le scénario est le même.

## Les différences point par point

| | Couches | Hexagonale |
|---|---|---|
| Image | une pile : haut → bas | un centre et un pourtour : intérieur / extérieur |
| Le métier référence… | `Couches.Donnees` | **aucun projet** |
| L'interface d'accès aux données est définie par… | la couche données (`IComptesDao`) | le métier (port sortant `IComptes`) |
| …dans le vocabulaire de… | la persistance (`CompteEntite`, `Enregistrer`) | le métier (`Compte`, `Charger`, `Sauvegarder`) |
| L'entité | **anémique** (setters publics) ; la règle est dans le service | **riche** : `Compte.Retirer` protège son solde |
| Canaux d'entrée | un : la présentation | plusieurs, à égalité : API, CLI, tests |
| Remplacer le stockage | touche le métier (il manipule les entités de persistance) | écrire un adapter ; les **tests de contrat** prouvent l'équivalence |
| Tester le métier | avec un faux DAO… qui renvoie des `CompteEntite` | avec un adapter de test, sans base |
| Garde-fou | références non transitives | hexagone sans référence + test d'architecture |

## Côte à côte

```mermaid
flowchart LR
    subgraph C["COUCHES"]
        direction TB
        P0["Présentation"] --> M0["Métier<br/>ComptesService"] -->|"dépend de"| D0["Données<br/>définit IComptesDao"]
    end
    subgraph H["HEXAGONALE"]
        direction TB
        W1["Adapter Web"] --> M1["Hexagone<br/>définit IComptes"]
        J1["Adapter JSON"] -->|"dépend de"| M1
    end
    C ~~~ H
```

La flèche entre le métier et les données **change de sens** : c'est l'inversion des dépendances.

## Dans le code des démos

**Qui définit le contrat d'accès aux données.** Dans les couches, c'est la couche données, avec ses propres types
([ComptesDao.cs](../../00-Couches/Couches.Donnees/ComptesDao.cs)) :

```csharp
// L'interface est définie PAR la couche données, dans SON vocabulaire (CompteEntite).
public interface IComptesDao
{
    Task<CompteEntite?> ParId(Guid id);
    Task Ajouter(CompteEntite compte);
    Task<bool> EstVide();
    Task Enregistrer();
}
```

Dans l'hexagonale, c'est le métier, qui dit ce dont il a besoin
([PortsSortants.cs](../../01-Hexagonale/Hexa.Hexagone/Ports/Sortants/PortsSortants.cs)) :

```csharp
public interface IComptes
{
    Task<Compte?> Charger(Guid id);
    Task Sauvegarder(Compte compte);
}
```

**Où vit la règle.** Couches : dans le service, sur une entité que n'importe qui peut modifier
([ComptesService.cs](../../00-Couches/Couches.Metier/ComptesService.cs)) :

```csharp
// L'entité étant anémique, rien n'empêche une autre classe de faire « compte.Solde -= x »
// sans passer par cette vérification : la règle n'est protégée que par la discipline.
private static void VerifierRetraitPossible(CompteEntite compte, decimal montant)
```

Hexagonale : dans l'entité elle-même, dont le solde n'a pas de setter public
([Compte.cs](../../01-Hexagonale/Hexa.Hexagone/Domaine/Compte.cs)) :

```csharp
public decimal Solde { get; private set; }

public void Retirer(decimal montant)
{
    VerifierMontant(montant);
    if (montant > Solde)
        throw new RegleMetierException("Solde insuffisant.");
    Solde -= montant;
}
```

## Le même retrait

| Étape | Couches | Hexagonale |
|---|---|---|
| Entrée | `ComptesController` (seule entrée possible) | adapter Web **ou** adapter CLI, même cas d'utilisation |
| Charger | `dao.ParId` → `CompteEntite` | `comptes.Charger` → `Compte` du domaine |
| Décider | `ComptesService.VerifierRetraitPossible` | `Compte.Retirer` |
| Modifier | `compte.Solde -= montant` dans le service | dans `Compte.Retirer` |
| Enregistrer | `dao.Enregistrer()` (le `SaveChanges` d'EF) | `comptes.Sauvegarder(compte)` : mémoire ou fichier JSON selon la configuration |

## Comment choisir

- **Couches** si l'application est surtout du CRUD, que les règles tiennent en quelques `if` et que la base ne
  changera pas : c'est simple et connu de tous.
- **Hexagonale** dès que l'un de ces signaux apparaît : des règles qui se multiplient, l'envie de les tester sans base,
  un deuxième canal d'entrée (CLI, messages), un stockage susceptible de changer.

## Passer des couches à l'hexagonale

1. Rendre l'entité riche : déplacer les règles du service dans des méthodes de l'entité, retirer les setters publics.
2. Déplacer l'interface d'accès aux données **dans le projet métier**, la renommer dans le vocabulaire du métier.
3. Faire implémenter cette interface par l'ancienne couche données (qui référence désormais le métier).
4. Retirer la référence du métier vers les données ; un test d'architecture l'empêche de revenir.

Ces étapes peuvent se faire **une fonctionnalité à la fois** : rien n'oblige à tout migrer d'un coup.

## Idées reçues

- *« L'hexagonale a six côtés pour six ports »* : non, la forme ne sert qu'à laisser de la place pour dessiner. On peut
  avoir deux ports ou vingt.
- *« Les couches, c'est forcément mal »* : pour un CRUD, l'inversion des dépendances ne rapporte rien et coûte des
  interfaces. Les couches restent un très bon point de départ.

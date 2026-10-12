# Acteurs ou Spatiale ?

**En anglais** : *Actor Model or Space-Based Architecture?*

> **La différence en une phrase** : les deux gardent l'état **en mémoire**, mais les acteurs donnent à chaque donnée
> **un seul propriétaire** (aucune écriture ne se perd), quand l'architecture spatiale la **réplique** dans chaque
> unité (des écritures simultanées peuvent se perdre).

📁 Démos : [`18-ActorModel`](../../18-ActorModel) · [`19-SpaceBased`](../../19-SpaceBased) · · 🌐 [English](../en/differences/16-acteurs-vs-spatiale.md)
📖 Pages : [Acteurs](../18-modele-acteurs.md) · [Spatiale](../19-space-based.md) ·
🗂️ Fiches : [18](../fiches/18-modele-acteurs.md) · [19](../fiches/19-space-based.md)

⬅️ [Cellules ou Microservices ?](15-cellules-vs-microservices.md) · ➡️ [Ce qu'on place devant les services](17-passerelle-bff-graphql-strangler.md)

---

## Ce qu'elles partagent

L'état est en mémoire, la base sert à **recharger** après une panne ; une requête n'attend pas un aller-retour vers la
base pour lire ; on passe à l'échelle en ajoutant des unités de calcul. Et dans les deux démos, une même expérience
(des centaines de retraits simultanés sur le compte d'Alice) révèle tout.

## Les différences point par point

| | Acteurs | Spatiale |
|---|---|---|
| Problème visé | la **concurrence** : plusieurs requêtes sur la même donnée | la **base qui sature** : trop de requêtes pour elle |
| Une donnée vit… | dans **un seul** acteur (`account/alice`) | **répliquée** dans chaque unité de traitement |
| Écritures simultanées | **mises en file** dans la boîte aux lettres, traitées une par une | appliquées **en parallèle** dans plusieurs répliques |
| Écriture perdue ? | **jamais** | **possible** (collision : la plus grande version gagne) |
| Parade | structurelle : un propriétaire, un message à la fois | le **routage par affinité** (même clé → même unité), imparfait |
| La base | état durable écrit par l'acteur ; relu par une nouvelle incarnation | sauvegarde **en différé** (pompe) ; peut tomber sans arrêter le service |
| Passer à l'échelle | des millions d'acteurs, **activés à la demande**, répartis | 2 à 6 unités, ajoutées selon la charge, chacune avec **toutes** les données |
| Une panne | l'acteur fautif est **remplacé** (supervision) ; les autres ne voient rien | une unité tombe : ses requêtes vont ailleurs ; toutes tombent : démarrage à froid depuis la base |
| Opération sur deux données | une conversation (acteur `transfer/…`), avec compensation | sans transaction ; risque de collision sur chaque clé |
| Limite | lectures transversales, cycles de demandes (interblocages) | tout en mémoire, base en retard, collisions |

## Côte à côte : 3 requêtes sur le compte d'Alice

```mermaid
flowchart LR
    subgraph A["ACTEURS : un propriétaire"]
        direction TB
        R1["retrait 1"] & R2["retrait 2"] & R3["retrait 3"] --> B["boîte aux lettres"]
        B --> AA["compte/alice<br/>un message à la fois"]
    end
    subgraph S["SPATIALE : des répliques"]
        direction TB
        Q1["retrait 1"] --> U1["u1 : Alice (copie)"]
        Q2["retrait 2"] --> U2["u2 : Alice (copie)"]
        U1 <-->|"réplication :<br/>la plus grande version gagne"| U2
    end
    A ~~~ S
```

## Dans le code des démos

**Acteurs** : un seul lecteur par boîte aux lettres, qui attend la fin de chaque message avant le suivant
([ActorSystem.cs](../../18-ActorModel/Actors.Runtime/ActorSystem.cs)) :

```csharp
// THE key point of the model: only one message at a time, each handled to the end before the next one.
response = await activation.Instance!.Receive(envelope.Message);
```

Le compte peut donc écrire le code le plus naïf : lire, vérifier, modifier, sans verrou
([AccountActor.cs](../../18-ActorModel/Actors.Bank/AccountActor.cs)) :

```csharp
if (debit && amount > state.Balance)
    return new Refusal($"Insufficient balance ({state.Balance:0.00} €).");
```

**Spatiale** : l'écriture est appliquée dans la réplique locale, puis **publiée** vers les autres répliques et la
pompe, sans les attendre ([DataGrid.cs](../../19-SpaceBased/Space.Grid/DataGrid.cs)) :

```csharp
// The database is not on the request path: the write goes into the pump, we do not wait.
_toThePump(modification);
```

À la réception, une réplique détecte qu'une modification ne part pas de la version qu'elle connaît : c'est une
collision, et la plus grande version gagne ([Replica.cs](../../19-SpaceBased/Space.Grid/Replica.cs)) :

```csharp
var concurrent = local != m.Previous && local.Unit != "" && local.Unit != m.Version.Unit;
```

La parade est de router une même clé toujours vers la même unité
([MessagingGrid.cs](../../19-SpaceBased/Space.Grid/MessagingGrid.cs)) :

```csharp
: _units.MaxBy(u => Hash($"{key}/{u.Id}"))!;
```

## La même expérience, deux résultats

| Essai réel | Acteurs | Spatiale |
|---|---|---|
| Requêtes | 1 000 retraits de 3 € simultanés (Alice : 1 000 €) | 200 retraits de 1 € simultanés (Alice : 1 000 €) |
| Résultat | 333 acceptés, solde final 1 € : **exact** | **affinité** : 200 acceptés, 800 € : exact |
| | | **tourniquet** : 200 acceptés, 700 € : **100 € perdus**, 199 mises à jour écrasées |

En spatiale, même l'affinité n'est pas une garantie : pendant qu'une unité arrive ou part, une clé peut changer de
propriétaire et une écriture se perdre (test `With_affinity_a_change_of_units_opens_a_collision_window`).
Les acteurs n'ont pas ce problème, car **le propriétaire est la donnée elle-même**.

## Comment choisir

- **Acteurs** pour beaucoup d'entités indépendantes, à l'état **très disputé** et souvent modifié : comptes, paniers,
  joueurs, objets connectés, sessions.
- **Spatiale** pour des **pics de charge énormes** que la base ne suit pas, sur des données qui tiennent en mémoire et
  sont **lues souvent, écrites rarement** ou bien partitionnées : billetterie, enchères, réservations. Mauvais choix
  pour de vrais soldes, et la démo s'en sert justement pour **montrer** les collisions.

## Les combiner

Les deux se rejoignent quand on passe d'un cache **répliqué** à un cache **distribué** : chaque donnée a alors une seule
copie maîtresse, sur une seule unité. C'est déjà presque un acteur (Orleans en est l'exemple : des acteurs répartis,
dont l'annuaire dit quel serveur héberge lequel).

## Idées reçues

- *« En mémoire = plus de base »* : les deux démos écrivent l'état durable ; seule la place de la base sur le chemin
  de la requête change.
- *« Les acteurs suppriment les problèmes du distribué »* : pas de transaction entre acteurs, et un cycle de demandes
  (A attend B qui attend A) les bloque jusqu'au délai.

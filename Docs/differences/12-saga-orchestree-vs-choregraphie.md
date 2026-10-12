# Saga orchestrée ou Chorégraphie ?

**En anglais** : *Orchestrated Saga or Event-Driven Choreography?*

> **La différence en une phrase** : le **même virement en plusieurs étapes**, écrit **en un seul endroit** (un
> orchestrateur qui appelle chaque service) ou **nulle part** (chaque service réagit aux événements des autres).

📁 Démos : [`14-OrchestratedSaga`](../../14-OrchestratedSaga) · [`21-Choreography`](../../21-Choreography) (mêmes données, · 🌐 [English](../en/differences/12-saga-orchestree-vs-choregraphie.md)
mêmes règles, pour comparer sur pièces) · 📖 Pages : [Saga orchestrée](../14-saga-orchestree.md) ·
[Chorégraphie](../21-choregraphie.md) · 🗂️ Fiches : [14](../fiches/14-saga-orchestree.md) · [21](../fiches/21-choregraphie.md)

⬅️ [Microkernel ou Microservices ?](11-microkernel-vs-microservices.md) · ➡️ [Pipeline ou Chorégraphie ?](13-pipeline-vs-choregraphie.md)

---

## Ce qu'elles partagent

Un processus réparti sur plusieurs services, donc **pas de transaction unique** : des étapes locales, des
**compensations** en cas d'échec. Dans les deux démos : les fonds sont d'abord **réservés**, un analyste humain décide
au-delà de 10 000 €, le change convertit, le **débit définitif est le point de non-retour** (après lui, on réessaie,
on ne défait plus), les participants sont idempotents.

## Les différences point par point

| | Saga orchestrée | Chorégraphie |
|---|---|---|
| Le processus est écrit… | **en un seul fichier** (`TransferDefinition.cs`) | **nulle part** : il émerge des abonnements |
| Un service connaît… | l'orchestrateur connaît **tous** les participants | **personne** : seulement des contrats d'événements (test d'architecture) |
| « Où en est le virement 42 ? » | l'**état de la saga**, enregistré après chaque pas | une **projection** à construire (le service Suivi, qui écoute les 13 types d'événements) |
| Compensation | **ordre inverse**, par le moteur, réessayée jusqu'à réussir | **dispersée** : Comptes libère les fonds sur `ComplianceRefused` **et** sur `ExchangeFailed` |
| Un participant en panne | le moteur réessaie (4 fois), puis compense ou passe « à traiter manuellement » | **silence** : le courtier réessaie, met aux lettres mortes ; la réservation **expire** au bout de 30 s |
| Attente humaine | une étape « en attente », avec une **échéance** gérée par le moteur | la Conformité attend ; elle doit aussi écouter `ReservationExpired` |
| Ajouter une réaction (fidélité…) | modifier la définition | **s'abonner**, sans toucher aux autres |
| Point unique de défaillance | l'orchestrateur | le courtier |
| La carte du système | la définition | la liste des abonnements, **à l'exécution** (`GET /bus`) |

## Côte à côte

```mermaid
flowchart LR
    subgraph O["ORCHESTRATION"]
        direction TB
        OR["Orchestrateur<br/>état de la saga"] -->|"① réserver · ④⑤ créditer, débiter"| C1["Accounts"]
        OR -->|"② examiner"| K1["Compliance"]
        OR -->|"③ convertir"| X1["Exchange"]
        OR -->|"⑥ notifier"| N1["Notifications"]
    end
    subgraph C["CHORÉGRAPHIE"]
        direction TB
        C2["Accounts"] -.->|"FundsReserved"| K2["Compliance"]
        K2 -.->|"ComplianceApproved"| X2["Exchange"]
        X2 -.->|"CurrenciesConverted"| C3["Accounts"]
        C3 -.->|"TransferSettled"| N2["Notifications, Loyalty"]
    end
    O ~~~ C
```

## Dans le code des démos

**Orchestration** : le processus se lit de haut en bas, avec ses compensations à côté de chaque étape
([TransferDefinition.cs](../../14-OrchestratedSaga/Saga.Orchestrator/TransferDefinition.cs)) :

```csharp
new("ReserveFunds",
    async s => { await accounts.Reserve(s.Id, s.Source, s.Amount); return Outcome.Success; },
    Compensate: s => accounts.Release(s.Id, s.Source)),
// …
new("CaptureFunds",
    async s => { await accounts.Capture(s.Id, s.Source); return Outcome.Success; },
    PointOfNoReturn: true),
```

Et c'est le moteur qui décide quoi faire d'un échec
([SagaEngine.cs](../../14-OrchestratedSaga/Saga.Orchestrator/SagaEngine.cs)) :

```csharp
else if (PointOfNoReturnPassed(saga))
{
    // Nothing can be undone any more: the money has already been debited for good.
    RequireManualIntervention(saga, $"{step.Name} impossible after {options.MaxAttempts} attempts: {message}");
}
else
{
    StartCompensation(saga, $"{step.Name} impossible after {options.MaxAttempts} attempts: {message}");
}
```

**Chorégraphie** : aucun fichier ne décrit le processus. Le seul fil est la **corrélation** et la **cause** portées
par chaque événement, qui permettent de le raconter après coup
([Bus.cs](../../21-Choreography/Choreo.Bus/Bus.cs)) :

```csharp
/// <summary>A new event, in reaction to this one: same correlation, and this one as cause.</summary>
public T Next<T>(T next, string emitter) where T : Event =>
    next with { Correlation = Correlation, Cause = Id, Emitter = emitter };
```

Quand le change tombe en panne, personne n'attend sa réponse. Le filet de sécurité est **local** : Comptes donne une
durée de vie à ses réservations et émet `ReservationExpired`. Chaque cas tardif (analyste qui décide trop tard,
règlement qui arrive après l'expiration) a demandé une ligne de code dans **un service différent**.

En revanche, ajouter le service Fidélité n'a demandé **aucune** modification ailleurs : il s'abonne à
`TransferSettled`, et le test `Adding_a_subscriber_changes_nothing_for_the_others` le prouve. Côté orchestration, il
aurait fallu ajouter une étape à la définition.

## Le même incident : le change est en panne

| | Saga orchestrée | Chorégraphie |
|---|---|---|
| Qui s'en aperçoit | le moteur (exception à l'étape ③) | le courtier (livraison en échec) |
| Ce qui se passe | nouveaux essais, puis compensation : fonds libérés, saga « Annulée » | 4 essais, lettres mortes ; le virement reste « EnCours », 200 € réservés |
| Comment ça finit | immédiatement, par le moteur | 30 s plus tard, par l'expiration de la réservation : « Abandonné » |
| Où le voir | `GET /sagas/{id}` : l'historique complet | `GET /bus/dead-letters`, `GET /transfers/stuck` |

## Comment choisir

- **Orchestration** pour **conduire** un processus métier à plusieurs étapes, avec attentes, délais et règles d'échec
  précises (commande, virement international, souscription). En production : Temporal, Durable Functions, Step
  Functions, sagas MassTransit / NServiceBus.
- **Chorégraphie** pour les **réactions** à un fait (notifier, facturer, archiver, alimenter un tableau de bord) et les
  processus courts, stables, à peu de participants.

## Les combiner

Le plus courant : un orchestrateur conduit le processus, puis publie `TransferMade` ; notifications, fidélité,
comptabilité **chorégraphient** autour, sans que l'orchestrateur les connaisse.

## Idées reçues

- *« La chorégraphie est plus découplée, donc meilleure »* : le couplage n'a pas disparu, il est devenu **implicite**
  (qui doit écouter quoi pour que le processus se termine).
- *« L'orchestrateur est un point de défaillance unique »* : le courtier l'est tout autant en chorégraphie ; et
  l'état de la saga, enregistré à chaque pas, permet de reprendre après un plantage.

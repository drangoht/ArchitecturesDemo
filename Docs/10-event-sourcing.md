# 10. Event Sourcing

> **En une phrase** : on ne stocke pas l'état, mais **la suite des événements** qui y ont mené ; l'état se recalcule en
> les rejouant, et on n'efface jamais rien.

📁 Code : [`10-EventSourcing`](../10-EventSourcing) · ⬅️ [CQRS](09-cqrs.md) · ➡️ [DDD](11-ddd.md)

---

## L'idée

Dans toutes les autres démos, on stocke **le solde** et on oublie comment on y est arrivé. Ici, on stocke les **faits** :
le compte a été ouvert avec 1 000 €, 100 € ont été retirés, 250 € ont été virés… Le solde n'est qu'un **calcul**.
C'est exactement ainsi que fonctionne une comptabilité : on n'efface jamais une écriture, on en ajoute une autre.

```mermaid
flowchart LR
    E1["v1 · CompteOuvert<br/>Alice, 1 000 €"] --> E2["v2 · ArgentRetire<br/>100 €, « Courses »"]
    E2 --> E3["v3 · ArgentRetire<br/>300 €, « Loyer »"]
    E3 --> E4["v4 · VirementEmis<br/>250 € vers Bob"]
    E4 -. "rejouer v1→v4" .-> S["État actuel<br/>solde : 350 €<br/>version : 4"]
    E2 -. "rejouer v1→v2" .-> T["État à la version 2<br/>solde : 900 €"]

    classDef evt fill:#fff8c5,stroke:#9a6700
    class E1,E2,E3,E4 evt
```

### Le cycle d'une commande

```mermaid
flowchart TB
    C["Commande<br/>POST /comptes/alice/retrait"] --> L["① CHARGER<br/>lire le flux d'Alice<br/>(depuis le dernier instantané)"]
    L --> R["② RECONSTITUER<br/>Appliquer chaque événement<br/>→ solde, version"]
    R --> D["③ DÉCIDER<br/>Retirer() vérifie les règles<br/>→ nouvel événement ArgentRetire"]
    D --> A["④ AJOUTER au magasin<br/>si la version est toujours<br/>celle qu'on a chargée"]
    A -->|"version a changé"| X["409 Conflit<br/>rien n'est écrit"]
    A -->|"ok"| M[("Magasin<br/>evenements.jsonl<br/>(ajout seul)")]
    M --> P["Projections<br/>(relevés, plus gros retraits…)"]
```

---

## Organisation de la solution

| Projet | Contenu |
|---|---|
| `Es.Domaine` | Événements, agrégat `Compte` (décider / appliquer), `InstantaneCompte` |
| `Es.Stockage` | `MagasinEvenements` (ajout seul, concurrence optimiste, fichier `.jsonl`), `SerialiseurEvenements` (+ upcasting), `DepotComptes` (instantanés, voyage dans le temps) |
| `Es.Projections` | `ProjectionReleves`, `ProjectionPlusGrosRetraits`, `MoteurProjections` (suivi, reconstruction) |
| `Es.Api` | Commandes, audit, voyage dans le temps, administration |

**La base de données est un fichier lisible** : `Es.Api\evenements.jsonl`, une ligne par événement.

```json
{"position":3,"flux":"1111…","version":2,"type":"ArgentRetire.v2","donnees":{"compteId":"1111…","montant":100,"motif":"Courses","date":"…"},"date":"…"}
```

---

## Le code, pas à pas

### 1. Décider / appliquer : deux sortes de méthodes, à ne jamais mélanger

([Compte.cs](../10-EventSourcing/Es.Domaine/Compte.cs))

```csharp
// DÉCISION : vérifie les règles, puis enregistre un événement.
public void Retirer(decimal montant, string motif, DateTime date)
{
    VerifierOuvert();
    VerifierMontant(montant);
    if (montant > Solde)
        throw new RegleMetierException("Solde insuffisant.");
    Enregistrer(new ArgentRetire(Id, montant, motif, date));
}

private void Enregistrer(IEvenement evenement)
{
    Appliquer(evenement);
    _nouveaux.Add(evenement);
}

// APPLICATION : fait évoluer l'état, SANS AUCUNE RÈGLE (un fait passé ne se refuse pas).
private void Appliquer(IEvenement evenement)
{
    switch (evenement)
    {
        case CompteOuvert e: (Id, Titulaire, Solde) = (e.CompteId, e.Titulaire, e.DepotInitial); break;
        case ArgentDepose e: Solde += e.Montant; break;
        case ArgentRetire e: Solde -= e.Montant; break;
        case VirementEmis e: Solde -= e.Montant; break;
        case VirementRecu e: Solde += e.Montant; break;
        case CompteCloture: Cloture = true; break;
    }
    Version++;
}
```

Charger un compte, c'est rejouer `Appliquer` sur tout son historique :

```csharp
public static Compte Reconstituer(IEnumerable<IEvenement> historique) => DepuisInstantane(null, historique);
```

```mermaid
stateDiagram-v2
    [*] --> Ouvert : CompteOuvert
    Ouvert --> Ouvert : ArgentDepose / ArgentRetire / VirementEmis / VirementRecu
    Ouvert --> Cloture : CompteCloture (seulement si solde = 0)
    Cloture --> [*]
    note right of Cloture : toute nouvelle opération est refusée
```

### 2. Le magasin : ajout seul et concurrence optimiste

([MagasinEvenements.cs](../10-EventSourcing/Es.Stockage/MagasinEvenements.cs))

```csharp
/// Ajoute les événements de un ou plusieurs flux, en tout ou rien.
/// Concurrence optimiste : si un flux a bougé depuis son chargement, rien n'est écrit.
public Task Ajouter(params AEcrire[] ecritures)
{
    lock (_verrou)
    {
        foreach (var ecriture in ecritures)
        {
            var actuelle = _evenements.Count(e => e.Flux == ecriture.Flux);
            if (actuelle != ecriture.VersionAttendue)
                throw new ConflitDeConcurrenceException(ecriture.Flux, ecriture.VersionAttendue, actuelle);
        }
        // … puis tout ajouter, en mémoire et dans le fichier .jsonl
    }
    return Task.CompletedTask;
}
```

```mermaid
sequenceDiagram
    actor A as Guichetier A
    actor B as Guichetier B
    participant API as Es.Api
    participant M as Magasin
    A->>API: GET /comptes/alice → version 1
    B->>API: GET /comptes/alice → version 1
    A->>API: retrait 80 € (If-Match: 1)
    API->>M: Ajouter(alice, versionAttendue: 1)
    M-->>API: ok → version 2
    B->>API: retrait 80 € (If-Match: 1)
    API-->>B: 409 Conflit « version attendue 1, version actuelle 2 »
    Note over B: B recharge le compte et décide<br/>avec des données à jour
```

### 3. Les instantanés : ne pas tout rejouer

Tous les 5 événements, une photo de l'état est mise en **cache**. On peut l'effacer sans rien perdre.
([DepotComptes.cs](../10-EventSourcing/Es.Stockage/DepotComptes.cs))

```csharp
public async Task<Compte> Charger(Guid id)
{
    var instantane = instantanes.Dernier(id);
    var suite = await magasin.LireFlux(id, apresVersion: instantane?.Version ?? 0);
    if (instantane is null && suite.Count == 0)
        throw new CompteIntrouvableException(id);
    return Compte.DepuisInstantane(instantane, suite.Select(e => e.Evenement()));
}
```

### 4. Le voyage dans le temps

```csharp
/// Voyage dans le temps : l'état du compte tel qu'il était à une version donnée.
public async Task<Compte> ChargerALaVersion(Guid id, int version)
{
    var historique = await magasin.LireFlux(id);
    // …
    return Compte.Reconstituer(historique.Where(e => e.Version <= version).Select(e => e.Evenement()));
}
```

`GET /comptes/{id}/etat?version=2` ou `?date=…` ; `GET /comptes/{id}/evenements` donne l'audit complet.

### 5. Faire évoluer un événement sans réécrire l'historique (*upcasting*)

`ArgentRetire` a gagné un champ `Motif` en v2. Les anciens événements v1 restent intacts ; ils sont convertis **à la lecture**.
([SerialiseurEvenements.cs](../10-EventSourcing/Es.Stockage/SerialiseurEvenements.cs))

```csharp
private static readonly Dictionary<string, Func<string, (string Type, string Json)>> Upcasters = new()
{
    // v1 : { compteId, montant, date }  →  v2 : + motif
    ["ArgentRetire"] = json =>
    {
        var noeud = JsonNode.Parse(json)!.AsObject();
        noeud["motif"] = "Non précisé";
        return ("ArgentRetire.v2", noeud.ToJsonString());
    }
};
```

### 6. Une projection ajoutée après coup connaît tout le passé

([Projections.cs](../10-EventSourcing/Es.Projections/Projections.cs))

```csharp
// ─── Plus gros retraits : une projection ajoutée APRÈS coup ───
// Comme elle se construit en rejouant tout le flux, elle connaît d'emblée tous les retraits passés,
// y compris ceux faits avant qu'elle existe. Impossible avec une base classique, qui n'a gardé que l'état final.
public class ProjectionPlusGrosRetraits : IProjection { /* … */ }
```

`POST /admin/projections/reconstruire` efface toutes les vues et rejoue tout le magasin.

---

## Les tests : Given / When / Then

Le style naturel de l'Event Sourcing : *étant donné* des faits, *quand* une commande arrive, *alors* on attend ces nouveaux faits.
([CompteTests.cs](../10-EventSourcing/Es.Tests/CompteTests.cs))

```csharp
private static IReadOnlyList<IEvenement> Quand(IEvenement[] etantDonne, Action<Compte> commande)
{
    var compte = Compte.Reconstituer(etantDonne);
    commande(compte);
    return compte.NouveauxEvenements;
}

[Fact]
public void Un_retrait_possible_produit_ArgentRetire()
{
    var alors = Quand(CompteAvec100Euros, c => c.Retirer(30m, "Courses", T));

    Assert.Equal([new ArgentRetire(Id, 30m, "Courses", T)], alors);
}
```

Autres tests ([StockageTests.cs](../10-EventSourcing/Es.Tests/StockageTests.cs), [ApiTests.cs](../10-EventSourcing/Es.Tests/ApiTests.cs)) :
conflit de concurrence, virement en tout ou rien sur deux flux, redémarrage depuis le fichier, upcasting, instantanés
(même résultat, moins d'événements lus), voyage dans le temps, reconstruction des projections.

---

## Bilan

| 👍 Points forts | 👎 Limites |
|---|---|
| **Audit complet** et immuable, par construction | On ne modifie ni ne supprime : erreurs corrigées par un événement compensatoire |
| **Voyage dans le temps** : l'état à n'importe quelle date | **RGPD** : prévoir l'effacement (chiffrer les données personnelles, puis détruire la clé) |
| Nouvelles vues **rétroactives** sur tout l'historique | Événements à **versionner** pour toujours (upcasters) |
| Concurrence explicite (versions) ; tests Given/When/Then | Projections à maintenir ; courbe d'apprentissage réelle |

**Quand l'utiliser** : quand l'historique est lui-même une donnée métier (banque, comptabilité, logistique, assurance,
audit réglementaire) — et **seulement dans ces modules**. Pour un CRUD, c'est excessif. En production : EventStoreDB,
Marten (PostgreSQL), Axon. Presque toujours combiné avec [CQRS](09-cqrs.md) pour les lectures.

# Pipeline ou Chorégraphie ?

**En anglais** : *Pipes and Filters or Event-Driven Choreography?*

> **La différence en une phrase** : dans un pipeline, des **données** traversent une suite d'étapes **fixée à
> l'assemblage**, dans un même processus ; dans une chorégraphie, des **faits** circulent entre services, et le chemin
> **émerge** des abonnements.

📁 Démos : [`20-Pipeline`](../../20-Pipeline) · [`21-Choregraphie`](../../21-Choregraphie) · · 🌐 [English](../en/differences/13-pipeline-vs-choregraphie.md)
📖 Pages : [Pipeline](../20-pipeline.md) · [Chorégraphie](../21-choregraphie.md) ·
🗂️ Fiches : [20](../fiches/20-pipeline.md) · [21](../fiches/21-choregraphie.md)

⬅️ [Saga orchestrée ou Chorégraphie ?](12-saga-orchestree-vs-choregraphie.md) · ➡️ [Multi-tenant ou Cellules ?](14-multi-tenant-vs-cellules.md)

---

## Ce qu'elles partagent

Des étapes **indépendantes** qui ne se connaissent pas, reliées par un intermédiaire (tuyau ou courtier) ; une étape
qui écarte un élément sans arrêter le reste ; des **reprises idempotentes** ; un ordre logique entre étapes que
personne n'appelle explicitement.

## Les différences point par point

| | Pipeline | Chorégraphie |
|---|---|---|
| Ce qui circule | des **données à transformer** : les lignes d'un fichier CSV | des **faits** : « fonds réservés », « devises converties » |
| Le chemin | **linéaire, fixé à l'assemblage** : Analyser → Valider → … → Exécuter | **émerge** des abonnements ; un fait peut déclencher plusieurs réactions |
| Portée | **un traitement**, dans un processus | **un processus métier**, entre services |
| L'intermédiaire | un tuyau **borné** (64 éléments) : plein, il fait attendre l'étage d'avant | un courtier qui **stocke** : pas de contre-pression |
| Débit | celui de l'étage le plus lent ; on **parallélise le goulot** (8 copies du contrôle de fraude) | chaque service à son rythme |
| Ordre | **préservé** (l'étage parallèle ré-ordonne) : la ligne 2 peut dépendre de la ligne 1 | aucune garantie entre virements différents |
| Un élément refusé | **rejeté avec un motif**, compté au bilan | un événement de refus (`ConformiteRefusee`), auquel d'autres réagissent |
| Une panne | une exception **arrête tout** le pipeline ; on rejoue le fichier | **silence** : lettres mortes, puis expiration |
| Ajouter une étape | réassembler le pipeline | s'abonner |
| Vue d'ensemble | le code d'assemblage | les abonnements, à l'exécution |

## Côte à côte

```mermaid
flowchart LR
    subgraph P["PIPELINE : un processus, un chemin"]
        direction TB
        F[/"CSV"/] --> A["Analyser"] --> V["Valider"] --> FR["Fraude ×N"] --> E["Exécuter"]
    end
    subgraph C["CHORÉGRAPHIE : des services, des réactions"]
        direction TB
        CO["Comptes"] -.->|"FondsReserves"| CF["Conformité"]
        CF -.->|"ConformiteApprouvee"| CH["Change"]
        CH -.->|"DevisesConverties"| CO
        CO -.->|"VirementRegle"| NO["Notifications"]
        CO -.->|"VirementRegle"| FI["Fidélité"]
    end
    P ~~~ C
```

## Dans le code des démos

**Pipeline** : un tuyau plein **bloque** l'étage qui écrit ; c'est ce qui borne la mémoire, quelle que soit la taille
du fichier ([Etage.cs](../../20-Pipeline/Pipeline.Noyau/Etage.cs)) :

```csharp
/// <summary>Writing into a full pipe BLOCKS: the stage waits for the next one to have room. That is the back-pressure.</summary>
private async Task Write(ChannelWriter<Element<TOutput>> output, Element<TOutput> element, RunContext context)
```

Et le premier étage en échec arrête tous les autres
([Pipeline.cs](../../20-Pipeline/Pipeline.Noyau/Pipeline.cs)) :

```csharp
// The first stage that fails stops all the others.
foreach (var task in tasks)
    _ = task.ContinueWith(_ => stopping.Cancel(), TaskContinuationOptions.OnlyOnFaulted);
```

**Chorégraphie** : chaque service garde les identifiants déjà traités, et réagit en publiant la suite ; rien ne le
ralentit si le suivant est lent, rien ne l'arrête si le suivant tombe
([Autres.cs](../../21-Choregraphie/Choreo.Services/Autres.cs)) :

```csharp
lock (Lock)
{
    if (_handled.Contains(received.Id))
        return;
    if (react() is { } next)
        Bus.Publish(next);
    _handled.Add(received.Id);
}
```

## Comment choisir

- **Pipeline** quand des données passent par une suite d'étapes indépendantes **à l'intérieur d'un traitement** :
  imports, exports, ETL, lots (paie, facturation), flux de journaux ou de médias, compilateurs.
- **Chorégraphie** quand des **services** doivent réagir à ce qui arrive ailleurs, sans se connaître.

## Les combiner

Un import de virements peut être un pipeline **dans** le service Comptes, et chaque virement exécuté publier un
événement que d'autres services chorégraphient. Les deux démos ne se font pas concurrence : elles vivent à deux
échelles différentes.

## Idées reçues

- *« Un pipeline, c'est séquentiel »* : tous les étages travaillent **en même temps**, chacun sur un élément différent.
- *« Mettre une file entre chaque étape, c'est un pipeline »* : sans tuyaux bornés, on perd la contre-pression, donc la
  mémoire bornée.

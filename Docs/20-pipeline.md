# 20. Pipeline (pipes & filters)

> **En une phrase** : un traitement est découpé en **filtres** indépendants, chacun faisant une seule chose, reliés par
> des **tuyaux** ; les données coulent de l'un à l'autre, chaque filtre travaillant **en même temps** que les autres sur
> un élément différent, comme une chaîne de montage.

📁 Code : [`20-Pipeline`](../20-Pipeline) · ⬅️ [Architecture spatiale](19-space-based.md) · ➡️ [Chorégraphie](21-choregraphie.md) · 🌐 [English](en/20-pipeline.md)

---

## L'idée : une chaîne de montage

C'est la plus ancienne des architectures de ce dépôt : celle du shell Unix (`cat transfers.csv | grep EUR | sort | uniq`),
des compilateurs (analyse lexicale → syntaxique → sémantique → génération), des ETL et des traitements de flux. Elle ne
s'applique pas à une application entière, mais à un **traitement de données** : ici, l'import d'un fichier de virements
envoyé par une entreprise (des milliers de lignes à contrôler puis exécuter).

```mermaid
flowchart LR
    F[/"Fichier CSV"/] --> A["Parse"] --> V["Validate"] --> D["Deduplicate"] --> C["ConvertCurrency"] --> FR["CheckFraud ×N"] --> E["Execute"] --> S(["Virements<br/>exécutés"])
    A -.-> R[("Rejets<br/>par motif")]
    V -.-> R
    D -.-> R
    C -.-> R
    FR -.-> R
    E -.-> R
```

Trois règles font l'architecture :

- un **filtre** ne connaît ni sa source, ni sa destination, ni les autres filtres : il reçoit un élément, en produit un
  autre, ou le **rejette** ;
- un **tuyau** relie deux filtres, et il est **borné** : quand il est plein, le filtre d'avant attend ;
- tous les filtres travaillent **en même temps** : pendant qu'*Exécuter* traite la ligne 1, *Contrôler la fraude* traite
  la 2, *Convertir* la 3…

| | Un traitement, c'est… | Pour l'accélérer… |
|---|---|---|
| Un service classique | une méthode qui fait tout, ligne après ligne | optimiser la méthode |
| **Pipeline** | une suite de filtres qui travaillent en parallèle | trouver le **goulot** et paralléliser **cet** étage |

---

## Les composants

- **Le moteur** ([Pipeline.Core](../20-Pipeline/Pipeline.Core), générique, sans package, sur `System.Threading.Channels`) :
  [`IFilter`](../20-Pipeline/Pipeline.Core/Filter.cs), les étages et leurs tuyaux bornés
  ([Stage.cs](../20-Pipeline/Pipeline.Core/Stage.cs)), l'assemblage et l'exécution ([Pipeline.cs](../20-Pipeline/Pipeline.Core/Pipeline.cs)).
- **Les filtres bancaires** ([Filters.cs](../20-Pipeline/Pipeline.Bank/Filters.cs)) et leur assemblage
  ([Imports.cs](../20-Pipeline/Pipeline.Bank/Imports.cs)).
- **L'API** ([Endpoints.cs](../20-Pipeline/Pipeline.Api/Endpoints.cs)) : le corps de `POST /imports` *est* le fichier,
  lu ligne à ligne pendant qu'il arrive.

Un filtre se résume à une méthode :

```csharp
public interface IFilter<in TInput, TOutput>
{
    string Name { get; }

    /// <summary>A rejection is a normal result (the line is discarded, the flow goes on); an exception stops the whole pipeline.</summary>
    ValueTask<Outcome<TOutput>> Process(TInput input, CancellationToken cancellation);
}
```

L'assemblage se lit comme le schéma, et le compilateur vérifie que la sortie de chaque filtre est l'entrée du suivant :

```csharp
Core.Pipeline.From<string>()
    .Then(new Parse())
    .Then(new Validate())
    .Then(new Deduplicate(ledger))
    .Then(new ConvertCurrency())
    .Then(new CheckFraud(options.ScoringCost), options.FraudParallelism, options.Ordered);
```

En production : **TPL Dataflow** ou les **Channels** directement dans un processus, **Akka Streams** ; à l'échelle d'un
système, les filtres deviennent des services et les tuyaux des files ou des flux (**Kafka Streams**, **Apache Beam**,
**Azure Data Factory**, les pipelines d'ETL et de CI).

---

## 1. Les rejets : écarter une ligne sans arrêter le fichier

Une ligne fautive n'interrompt pas l'import : le filtre qui la détecte la **rejette** avec un motif, et le flux continue.
Le bilan compte les rejets par motif et garde les premiers, avec leur ligne et le filtre qui les a écartés. **Essai
réel** avec [Transfers-sample.csv](../20-Pipeline/Pipeline.Api/Transfers-sample.csv) (11 lignes) :

```text
ligne 3   Parse              malformed line                unreadable amount "twelve"
ligne 4   Validate           invalid IBAN                  FR7630006000011234567890188
ligne 5   Validate           identical accounts
ligne 6   Deduplicate        duplicate                     in the file
ligne 7   Convert currency   unknown currency              JPY
ligne 8   Check fraud        payee on blocklist
ligne 9   Check fraud        ceiling exceeded              10530,00 €
ligne 10  Execute            insufficient balance          VIR-009
→ 3 virements exécutés : Alice 842,80 €, Bob 657,20 €
```

Une **exception**, elle, signale une panne : tout le pipeline s'arrête (le premier étage en échec annule les autres).

```csharp
// The first stage that fails stops all the others.
foreach (var task in tasks)
    _ = task.ContinueWith(_ => stopping.Cancel(), TaskContinuationOptions.OnlyOnFaulted);
```

---

## 2. Recomposer : les mêmes filtres, un autre pipeline

Les filtres ne se connaissant pas, on les réassemble à volonté. L'**import à blanc** (`POST /imports?dryRun=true`)
est le même pipeline **sans le dernier filtre** : il dit ce qui serait rejeté, sans toucher à un seul compte.

```csharp
public static Pipeline<string, Transfer> DryRun(Ledger ledger, ImportOptions options) => Checks(ledger, options).Build();
```

De même, la panne simulée de la démo est un filtre inséré avant *Exécuter*, et les tests assemblent leurs propres
pipelines avec des filtres de test. Deux conseils d'assemblage :

- **les filtres bon marché d'abord** : ce qu'*Analyser* ou *Dédupliquer* rejettent n'atteint pas le coûteux contrôle de fraude ;
- les filtres sont indépendants, mais le pipeline a un **ordre logique** : *Exécuter* suppose que *Convertir* est passé
  (montant en euros). Il vérifie donc sa précondition plutôt que de faire confiance à l'assemblage (test
  `Execute_refuses_a_transfer_that_did_not_go_through_the_conversion`, écrit après que ce piège s'est présenté).

---

## 3. La contre-pression : un fichier de taille quelconque, une mémoire bornée

Les tuyaux contiennent au plus 64 éléments. Quand un étage est plus lent que le précédent, son tuyau d'entrée se
remplit, et l'étage d'avant **attend** pour écrire ; l'attente remonte jusqu'à la lecture du fichier.

```csharp
/// <summary>Writing into a full pipe BLOCKS: the stage waits for the next one to have room. That is the back-pressure.</summary>
private async Task Write(ChannelWriter<Element<TOutput>> output, Element<TOutput> element, RunContext context)
{
    var start = Stopwatch.GetTimestamp();
    await output.WriteAsync(element, context.Cancellation);
```

Résultat : le fichier n'est jamais chargé en entier. **Essai réel**, 100 000 lignes générées à la volée : au plus
**330 à 375 lignes en mémoire** à la fois selon le parallélisme (`maxEnTransit`), et ce serait pareil pour 10 millions.

---

## 4. Le goulot, et le parallélisme d'un étage

Le débit d'un pipeline est celui de son **étage le plus lent**. Le bilan mesure, pour chaque étage, son temps de
traitement et son temps d'attente du tuyau suivant. **Essai réel**, 100 000 lignes :

```text
1 copie du contrôle de fraude : 4 117 ms (24 000 lignes/s)
   Parse         occupation 0,02   attente du suivant 3 976 ms
   Validate      occupation 0,05   attente du suivant 3 847 ms
   Check fraud   occupation 0,94   ← le goulot
   Execute       occupation 0,01
8 copies : 763 ms (131 000 lignes/s), même résultat
```

Les étages rapides passent leur temps à attendre : seul le goulot compte. Un filtre **sans état** (comme le contrôle de
fraude) peut tourner en **plusieurs copies** ; un filtre à état (*Dédupliquer*, *Exécuter*) reste unique. Et une fois le
goulot élargi, un autre prend sa place : avec un score de fraude léger, le goulot devient *Valider* (le calcul des IBAN),
et ajouter des copies de fraude ne sert plus à rien.

Le piège : plusieurs copies finissent dans le désordre. Or **l'ordre du fichier compte** : si Alice vire 600 € à Bob
ligne 1, et Bob 1 000 € ligne 2, la ligne 2 n'est possible qu'après la 1. L'étage parallèle numérote donc les éléments
et les fait ressortir dans l'ordre d'arrivée :

```csharp
while (early.Remove(expected, out var next))
{
    if (next is not null)
        await Write(output, next, context);
    expected++;
    window.Release();
}
```

Sans cela, la ligne 2 arrive la première à *Exécuter* et est rejetée (test
`The_file_order_matters_and_the_ordered_parallel_stage_respects_it`). Paralléliser ne change donc pas le résultat
(test `Parallelizing_the_fraud_check_does_not_change_the_result`).

---

## 5. La reprise après une panne

Un import peut s'arrêter au milieu (panne, déploiement, base indisponible). Une partie des lignes est exécutée, pas les
autres. Le remède est l'**idempotence** : *Dédupliquer* écarte toute référence déjà exécutée, et le registre refuse en
dernier recours d'exécuter deux fois la même. On **rejoue simplement le fichier**. Essai réel :

```text
POST /imports?failureAtReference=VIR-010   → 500, « Simulated failure while processing VIR-010. », bilan : 0 exécuté
GET  /accounts/<Alice>                     → 880 € : VIR-001 avait pourtant été exécuté
POST /imports                              → 2 exécutés, doublons : 2 ; Alice 842,80 €, Bob 657,20 € (comme sans panne)
```

⚠ Le bilan d'un import **échoué** dit ce qui est *sorti* du pipeline, pas ce qui a été *fait* : VIR-001 était exécuté,
mais sa sortie attendait encore dans le dernier tuyau quand l'arrêt a tout coupé. Seul le registre fait foi : c'est
pourquoi la reprise s'appuie sur les références exécutées, et non sur le bilan.

---

## Les tests

Le moteur avec des filtres de test ([CoreTests.cs](../20-Pipeline/Pipeline.Tests/CoreTests.cs)), les filtres
bancaires assemblés comme dans l'API ([ImportTests.cs](../20-Pipeline/Pipeline.Tests/ImportTests.cs)), et l'API
([ApiTests.cs](../20-Pipeline/Pipeline.Tests/ApiTests.cs)).

| Test | Ce qui est vérifié |
|---|---|
| `The_items_go_through_the_filters_in_order`, `A_rejection_discards_the_item_without_stopping_the_flow` | le flux, les rejets par motif |
| `The_stages_work_at_the_same_time_each_on_one_item` | 3 étages occupés en même temps |
| `Back_pressure_bounds_what_is_in_memory` | 20 000 éléments, au plus 37 en transit |
| `An_ordered_parallel_stage_returns_the_items_in_their_arrival_order` | ordonné ou non, avec un élément très lent |
| `An_exception_in_a_filter_stops_the_whole_pipeline`, `Cancellation_stops_an_endless_flow` | panne, annulation |
| `The_bottleneck_is_the_busiest_stage_and_each_run_has_its_counters` | mesures |
| `The_sample_file_gives_three_transfers_and_one_rejection_per_reason` | chaque rejet par le bon filtre, soldes |
| `Replaying_a_file_executes_nothing_twice`, `The_ledger_never_executes_the_same_reference_twice` | idempotence, à deux niveaux |
| `After_a_failure_in_the_middle_of_an_import_the_replay_finishes_without_executing_anything_twice` | reprise |
| `A_dry_run_import_tells_what_would_go_through_without_modifying_anything` | recomposition |
| `The_file_order_matters_and_the_ordered_parallel_stage_respects_it`, `Parallelizing_the_fraud_check_does_not_change_the_result` | ordre et déterminisme |
| `Execute_refuses_a_transfer_that_did_not_go_through_the_conversion` | précondition d'un filtre |
| `A_big_file_flows_through_with_bounded_memory` | 20 000 lignes, rejets attendus, argent conservé |
| `The_check_digits_of_an_iban_are_verified` | modulo 97 |
| `An_import_executes_the_file_and_keeps_its_report`, `A_dry_run_import_touches_no_account`, `A_failure_answers_500_with_the_report`, `The_bulk_import_demo_points_out_the_bottleneck` | l'API |

Vérifiés par mutation : tuyaux non bornés, étage parallèle jamais ordonné, panne sans arrêt des autres étages, chacune
des deux protections contre les doublons, précondition de conversion, rejet qui ne libère pas sa place, compteurs
partagés entre exécutions, en-tête du fichier non sauté, liste noire, comptage des rejets : chacun fait échouer au moins
un test.

---

## Bilan

| 👍 Points forts | 👎 Limites |
|---|---|
| Filtres **simples**, testables un par un, **réutilisables** et réassemblables | Pour des traitements en flux seulement, pas pour une application interactive |
| **Débit** : tous les étages en même temps ; on parallélise le goulot, et lui seul | Le débit est celui du plus lent ; chaque tuyau a un coût (copie, synchronisation) |
| **Mémoire bornée** par la contre-pression, quelle que soit la taille des données | Filtres à état (dédoublonnage, exécution) : uniques, et ordre à préserver |
| Rejets par motif, mesures par étage : on voit où ça coince | Une panne au milieu laisse un travail partiel : idempotence obligatoire pour rejouer |

**Quand l'utiliser** : imports et exports de fichiers, ETL, traitements par lots (relevés, paie, facturation),
traitement de flux d'événements, de médias ou de journaux, compilateurs. Partout où des données passent par une suite
d'étapes indépendantes. Ce n'est pas une architecture d'application à part entière : on loge un pipeline dans un
service ou un module, organisé lui-même selon une autre démo.

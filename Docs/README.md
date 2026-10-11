# Architectures logicielles en C# — documentation

Cette documentation explique **dix-neuf façons d'organiser une application**, chacune illustrée par une solution .NET 10
complète, testée et exécutable dans ce dépôt. Toutes implémentent **le même métier** — une petite banque : ouvrir un compte,
consulter, retirer, virer — pour qu'on puisse comparer la *forme* du code à fonctionnalités égales.

> **Lire les schémas.** Les diagrammes sont écrits en [Mermaid](https://mermaid.js.org/). Ils s'affichent directement sur
> GitHub / GitLab / Azure DevOps, dans Rider et Visual Studio 2022 (aperçu Markdown), et dans VS Code avec l'extension
> *Markdown Preview Mermaid Support*.

---

## Sommaire

| # | Architecture | En une phrase | Doc | Code |
|---|---|---|---|---|
| 0 | **Couches (N-tiers)** | Présentation → Métier → Données : les dépendances descendent vers la base | [00-couches](00-couches.md) | [`00-Couches`](../00-Couches) |
| 1 | **Hexagonale** (ports & adapters) | Le métier au centre, branché au monde par des ports et des adapters interchangeables | [01-hexagonale](01-hexagonale.md) | [`01-Hexagonale`](../01-Hexagonale) |
| 2 | **Onion** | Des couches concentriques, toutes les dépendances pointent vers le domaine | [02-onion](02-onion.md) | [`02-Onion`](../02-Onion) |
| 3 | **Clean Architecture** | Un interacteur par cas d'utilisation ; le résultat sort par un *presenter* | [03-clean](03-clean.md) | [`03-Clean`](../03-Clean) |
| 4 | **Functional Core, Imperative Shell** | Des règles 100 % pures qui décident ; une coquille qui fait les entrées/sorties | [04-coeur-fonctionnel](04-coeur-fonctionnel.md) | [`04-CoeurFonctionnel`](../04-CoeurFonctionnel) |
| 5 | **Vertical Slice** | Un fichier par fonctionnalité, de la route jusqu'à la base | [05-vertical-slice](05-vertical-slice.md) | [`05-VerticalSlice`](../05-VerticalSlice) |
| 6 | **Package by Component** | Une interface publique par composant, tout le reste `internal` | [06-composants](06-composants.md) | [`06-Composants`](../06-Composants) |
| 7 | **Monolithe modulaire** | Des modules autonomes, reliés par des contrats et des événements, dans un seul déploiement | [07-monolithe-modulaire](07-monolithe-modulaire.md) | [`07-MonolitheModulaire`](../07-MonolitheModulaire) |
| 8 | **Microservices** | Un processus par service, HTTP + messages, aucun code partagé | [08-microservices](08-microservices.md) | [`08-Microservices`](../08-Microservices) |
| 9 | **CQRS** | Un modèle pour écrire, un autre pour lire, reliés par des événements | [09-cqrs](09-cqrs.md) | [`09-CQRS`](../09-CQRS) |
| 10 | **Event Sourcing** | On stocke les événements, l'état se recalcule en les rejouant | [10-event-sourcing](10-event-sourcing.md) | [`10-EventSourcing`](../10-EventSourcing) |
| 11 | **Domain-Driven Design** | Modéliser le métier dans sa langue ; découper en contextes délimités | [11-ddd](11-ddd.md) | [`11-DDD`](../11-DDD) |
| 12 | **Serverless** | Des fonctions sans état déclenchées par des événements, facturées à l'usage | [12-serverless](12-serverless.md) | [`12-Serverless`](../12-Serverless) |
| 13 | **Microkernel** (plugins) | Un noyau minimal et des plugins chargés, déchargés et mis à jour à chaud | [13-microkernel](13-microkernel.md) | [`13-Microkernel`](../13-Microkernel) |
| 14 | **Saga orchestrée** | Un orchestrateur pilote un processus multi-services : état persistant, réessais, attentes, compensation | [14-saga-orchestree](14-saga-orchestree.md) | [`14-SagaOrchestree`](../14-SagaOrchestree) |
| 15 | **Multi-tenant** (SaaS) | Une seule application pour plusieurs clients : données isolées, règles et quotas par client | [15-multi-tenant](15-multi-tenant.md) | [`15-MultiTenant`](../15-MultiTenant) |
| 16 | **Backend for Frontend** (BFF) | Un backend par application cliente : agrégation, réponses taillées pour l'écran, jeton gardé côté serveur | [16-bff](16-bff.md) | [`16-BFF`](../16-BFF) |
| 17 | **Architecture cellulaire** (cell-based) | Plusieurs copies complètes du système, chacune pour une partie des clients : rayon d'impact borné | [17-cell-based](17-cell-based.md) | [`17-CellBased`](../17-CellBased) |
| 18 | **Modèle d'acteurs** | Des milliers d'acteurs à l'état privé, qui ne se parlent que par messages traités un par un ; on remplace ceux qui plantent | [18-modele-acteurs](18-modele-acteurs.md) | [`18-ModeleActeurs`](../18-ModeleActeurs) |
| ★ | **Concepts transverses** | Idempotence, outbox, concurrence optimiste, compensation, cohérence à terme | [concepts-transverses](concepts-transverses.md) | (démos 6 à 18) |

---

## Trois questions différentes

Ces « architectures » ne répondent pas toutes à la même question. Les mélanger dans un même classement est la principale
source de confusion. Elles se rangent en trois familles, **et se combinent** d'une famille à l'autre.

```mermaid
flowchart LR
    Q1["<b>① Comment organiser le code<br/>à l'intérieur d'une application ?</b>"]
    Q2["<b>② Comment découper le système<br/>en morceaux indépendants ?</b>"]
    Q3["<b>③ Comment modéliser<br/>le métier et les données ?</b>"]

    Q1 --> C0["Couches"]
    Q1 --> C1["Hexagonale"]
    Q1 --> C2["Onion"]
    Q1 --> C3["Clean"]
    Q1 --> C4["Cœur fonctionnel"]
    Q1 --> C5["Vertical Slice"]
    Q1 --> C13["Microkernel"]

    Q2 --> D6["Composants"]
    Q2 --> D7["Monolithe modulaire"]
    Q2 --> D8["Microservices"]
    Q2 --> D12["Serverless"]
    Q2 --> D14["Saga orchestrée"]
    Q2 --> D15["Multi-tenant"]
    Q2 --> D16["BFF"]
    Q2 --> D17["Cellules"]
    Q2 --> D18["Acteurs"]

    Q3 --> M11["DDD"]
    Q3 --> M9["CQRS"]
    Q3 --> M10["Event Sourcing"]

    classDef q fill:#1f6feb,color:#fff,stroke:#1f6feb
    class Q1,Q2,Q3 q
```

| Famille | Question | Granularité | Exemples de combinaison |
|---|---|---|---|
| **Organisation du code** | Où mettre quoi, qui dépend de qui, où peut-on étendre ? | une application, un module, un service | Vertical Slice *dans* un module ; Hexagonale *dans* un microservice ; plugins autour d'un noyau |
| **Découpage du système** | Quelles frontières, quel déploiement, quelle coordination ? | le système entier | Monolithe modulaire dont un module devient microservice ; saga orchestrée entre services ; une application partagée entre plusieurs clients ; un BFF par application devant des microservices ; des cellules contenant chacune un monolithe modulaire ; des acteurs à l'intérieur d'un service |
| **Modélisation** | Comment représenter le métier et l'état ? | un contexte, un module | DDD pour trouver les modules ; CQRS + Event Sourcing dans *un* module |

### Les deux grands axes de l'organisation du code

```mermaid
flowchart TB
    subgraph H["HORIZONTAL · par couche"]
        direction LR
        H1["Couches"] -->|"inversion des<br/>dépendances"| H2["Hexagonale<br/>Onion"] -->|"cas d'utilisation<br/>explicites"| H3["Clean"]
    end
    subgraph V["VERTICAL · par fonctionnalité"]
        direction LR
        V1["Vertical Slice"] -->|"internal"| V2["Composants"] -->|"contrats +<br/>événements"| V3["Monolithe<br/>modulaire"] -->|"réseau"| V4["Microservices"]
    end
    H ~~~ V
```

- **Horizontal** : on sépare *la technique* (UI, métier, données). Le progrès de *Couches* vers *Hexagonale / Onion / Clean*
  est l'**inversion des dépendances** : le métier cesse de dépendre de la base de données.
- **Vertical** : on sépare *les fonctionnalités ou les sous-domaines*. Le progrès de *Composants* vers *Microservices* est
  une **isolation de plus en plus forte** : visibilité `internal`, puis contrats, puis frontière réseau.

---

## Guide de choix

Aucun choix n'est « le bon » dans l'absolu. Cet arbre donne un **point de départ raisonnable** ; chaque page détaille
les signaux qui doivent faire évoluer le choix.

```mermaid
flowchart TD
    A{"Le métier est-il<br/>surtout du CRUD ?"} -->|oui| B["<b>Couches</b> ou <b>Vertical Slice</b><br/>simple et suffisant"]
    A -->|"non : règles riches"| C{"Plusieurs équipes /<br/>sous-domaines distincts ?"}
    C -->|non| D["<b>Hexagonale</b> / <b>Onion</b> / <b>Clean</b><br/>+ objets-valeurs du <b>DDD</b>"]
    C -->|oui| E["<b>DDD stratégique</b><br/>pour trouver les contextes"]
    E --> F{"Besoin de déployer /<br/>scaler séparément ?"}
    F -->|"non (cas le plus fréquent)"| G["<b>Monolithe modulaire</b>"]
    F -->|"oui, et l'équipe sait<br/>exploiter du distribué"| H["<b>Microservices</b>"]
    F -->|"charge très irrégulière,<br/>traitements événementiels"| S["<b>Serverless</b><br/>(pour ces traitements)"]
    G --> I{"Lectures très différentes<br/>ou bien plus nombreuses<br/>que les écritures ?"}
    H --> I
    I -->|oui| J["<b>CQRS</b> dans ce module"]
    J --> K{"L'historique est-il<br/>une donnée métier ?<br/>(audit, banque, compta)"}
    K -->|oui| L["<b>Event Sourcing</b> dans ce module"]
    D --> M{"Règles complexes<br/>difficiles à tester ?"}
    M -->|oui| N["<b>Cœur fonctionnel</b><br/>pour ces règles"]
    D --> P{"Variantes par client,<br/>extensions par des tiers ?"}
    P -->|oui| Q["<b>Microkernel</b><br/>(plugins)"]
    H --> T{"Processus en plusieurs étapes<br/>entre services, avec attentes<br/>et règles d'échec ?"}
    G --> T
    T -->|oui| U["<b>Saga orchestrée</b>"]
    T -->|"non : simples notifications"| V["chorégraphie<br/>(événements)"]
    G --> W{"Le même logiciel vendu à<br/>plusieurs organisations ?"}
    W -->|oui| X["<b>Multi-tenant</b><br/>(base partagée, puis dédiée<br/>pour qui la paie)"]
    H --> Y{"Plusieurs applications clientes<br/>(site, mobile) aux besoins<br/>différents ?"}
    Y -->|oui| Z["<b>BFF</b><br/>(un par application)"]
    H --> CA{"Très grande échelle :<br/>une panne générale coûte<br/>plus que N copies ?"}
    CA -->|oui| CB["<b>Cellules</b><br/>(partition par client)"]
    G --> AC{"Beaucoup d'entités indépendantes,<br/>à l'état modifié souvent<br/>et en concurrence ?"}
    H --> AC
    AC -->|oui| AD["<b>Acteurs</b><br/>(un par entité)"]
```

---

## Comparatif

| | Dépendance du métier | Ce qu'on stocke | Déploiements | Garde-fou | Coût principal |
|---|---|---|---|---|---|
| [Couches](00-couches.md) | dépend de la couche données | l'état | 1 | références non transitives | métier couplé à la persistance |
| [Hexagonale](01-hexagonale.md) | rien | l'état | 1 | discipline + test d'archi | interfaces à maintenir |
| [Onion](02-onion.md) | rien | l'état | 1 | références de projets | nombre de projets |
| [Clean](03-clean.md) | rien | l'état | 1 | références de projets | cérémonie (boundaries, presenters) |
| [Cœur fonctionnel](04-coeur-fonctionnel.md) | rien (pur) | l'état | 1 | séparation des projets | tout charger *avant* de décider |
| [Vertical Slice](05-vertical-slice.md) | du `DbContext` | l'état | 1 | discipline | duplication si on ne refactore pas |
| [Composants](06-composants.md) | rien hors du composant | l'état | 1 | **compilateur** (`internal`) | interfaces à concevoir |
| [Monolithe modulaire](07-monolithe-modulaire.md) | contrats des autres modules | l'état (1 base / module) | 1 | `internal` + Contrats + test d'archi | pas de transaction entre modules |
| [Microservices](08-microservices.md) | JSON des autres services | l'état (1 base / service) | **N** | **le réseau** | pannes, idempotence, exploitation |
| [CQRS](09-cqrs.md) | rien (côté écriture) | l'état + des vues | 1 | références de projets | cohérence à terme, données dupliquées |
| [Event Sourcing](10-event-sourcing.md) | rien | **les événements** | 1 | magasin en ajout seul | versionnement, RGPD, courbe d'apprentissage |
| [DDD](11-ddd.md) | rien | l'état | 1 | langage + contextes | exige des experts métier disponibles |
| [Serverless](12-serverless.md) | services gérés du fournisseur | l'état (services gérés) | **1 par fonction** | **la plateforme** | démarrages à froid, dépendance au fournisseur |
| [Microkernel](13-microkernel.md) | rien (les plugins dépendent du SDK) | l'état | 1 (+ plugins déposés à chaud) | contexte de chargement par plugin | SDK à versionner, plugins dans le processus |
| [Saga orchestrée](14-saga-orchestree.md) | des participants, via l'orchestrateur | l'état + l'état de chaque saga | 1 orchestrateur + N participants | moteur : réessais, échéances, compensation | participants idempotents, orchestrateur à exploiter |
| [Multi-tenant](15-multi-tenant.md) | rien (le locataire est ambiant) | l'état, étiqueté par locataire | **1 pour tous les clients** | filtre global + estampillage + clé composite | fuite entre clients au moindre oubli, voisin bruyant |
| [BFF](16-bff.md) | des services en aval (HTTP) | rien, sauf les sessions | 1 par application cliente | aucune référence entre BFF ; droits vérifiés par les services | un composant par application, duplication, métier qui s'y glisse |
| [Cellules](17-cell-based.md) | rien (chaque cellule est autonome) | l'état, réparti entre cellules | 1 routeur + **N cellules**, par vagues | **la cellule** : rayon d'impact borné | échanges inter-cellules, N fois l'infrastructure, migrations |
| [Acteurs](18-modele-acteurs.md) | rien (un acteur ne connaît que des adresses) | l'état, une ligne par acteur | 1 (ou une grappe de serveurs : Orleans, Akka) | **la boîte aux lettres** : un message à la fois ; le superviseur | pas de transaction entre acteurs, lectures transversales, cycles de demandes |

---

## Le fil rouge : un virement

Pour comparer, gardez en tête ce scénario, présent dans toutes les démos : *Alice vire 250 € à Bob*.
Chaque page montre son trajet dans l'architecture concernée.

```mermaid
sequenceDiagram
    actor Client
    participant Systeme as Application
    Client->>Systeme: POST /virements (Alice → Bob, 250 €)
    Systeme->>Systeme: vérifier les règles (montant, solde, comptes différents…)
    Systeme->>Systeme: débiter Alice, créditer Bob
    Systeme-->>Client: résultat (200, 202, 400, 404…)
    Note over Systeme: Selon l'architecture : synchrone ou non,<br/>une ou plusieurs bases, un ou plusieurs processus,<br/>avec ou sans événements, avec ou sans compensation.
```

Données de démo communes : **Alice** `11111111-1111-1111-1111-111111111111` (1 000 €) et
**Bob** `22222222-2222-2222-2222-222222222222` (500 €).

---

## Lancer les démos

```powershell
dotnet test ArchitecturesDemo.slnx                          # les 281 tests
dotnet run --project 01-Hexagonale\Hexa.Adapters.Web        # une API (ports 5100 à 5118)
dotnet run --project 08-Microservices\Micro.Lanceur         # les 5 microservices
```

Chaque API contient un fichier **`Banque.http`** avec les requêtes du scénario, à exécuter depuis Visual Studio, Rider
ou VS Code (extension REST Client). Le [README principal](../README.md) détaille les points à observer pour chaque démo.

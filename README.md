# Démos d'architectures en C#

Le **même métier bancaire** (consulter un compte, retirer de l'argent, faire un virement) implémenté avec vingt et une architectures,
plus une démo de conception pilotée par le domaine (DDD), une démo de migration progressive d'un ancien système (Strangler Fig)
et une démo qui expose le même métier de trois façons (REST, gRPC, GraphQL). On peut ainsi comparer la **forme** du code à fonctionnalités égales.
La démo 0 (couches classiques) sert de point de comparaison : toutes les autres sont nées pour corriger l'une de ses limites.

📘 **Documentation complète, avec schémas : [`Docs/`](Docs/README.md)**.

| # | Dossier | Architecture | Idée clé | Port |
|---|---|---|---|---|
| 0 | `00-Couches` | Couches / N-tiers (classique) | Présentation → Métier → Données : les dépendances descendent vers la base | 5100 |
| 1 | `01-Hexagonale` | Ports & Adapters (Cockburn) | Le métier au centre, branché au monde par des ports (interfaces) et des adapters | 5101 |
| 2 | `02-Onion` | Onion (Palermo) | Couches concentriques ; le centre distingue modèle et services de domaine | 5102 |
| 3 | `03-Clean` | Clean Architecture (Uncle Bob) | Un interacteur par cas d'utilisation ; le résultat passe par un presenter | 5103 |
| 4 | `04-CoeurFonctionnel` | Functional Core / Imperative Shell | Règles 100 % pures qui renvoient une décision ; la coquille fait les I/O | 5104 |
| 5 | `05-VerticalSlice` | Vertical Slice (Bogard) | Un fichier par fonctionnalité, de la route à la base | 5105 |
| 6 | `06-Composants` | Package by Component (Simon Brown) | Une interface publique par composant, tout le reste `internal` | 5106 |
| 7 | `07-MonolitheModulaire` | Monolithe modulaire | Modules autonomes (API, base, événements), reliés par des contrats, dans un seul déploiement | 5107 |
| 8 | `08-Microservices` | Microservices | Un processus par service, communication par HTTP et par messages, aucun code partagé | 5200 (passerelle) |
| 9 | `09-CQRS` | CQRS | Un modèle pour écrire (règles), un autre pour lire (vues prêtes à servir), reliés par des événements | 5108 |
| 10 | `10-EventSourcing` | Event Sourcing | On ne stocke pas l'état mais la suite des événements ; l'état se recalcule en les rejouant | 5109 |
| 11 | `11-DDD` | Domain-Driven Design | Modéliser le métier dans son langage : objets-valeurs, agrégats, contextes délimités, anti-corruption | 5110 |
| 12 | `12-Serverless` | Serverless (FaaS) | Des fonctions sans état déclenchées par des événements ; le fournisseur gère les serveurs et facture à l'usage | 5111 |
| 13 | `13-Microkernel` | Microkernel (plugins) | Un noyau minimal ; contrôles, frais et formats de relevé sont des plugins chargés à chaud | 5112 |
| 14 | `14-SagaOrchestree` | Saga orchestrée | Un orchestrateur conduit un virement en 6 étapes : état persistant, réessais, attente, compensation | 5113 |
| 15 | `15-MultiTenant` | Multi-tenant (SaaS) | Une seule application pour plusieurs banques clientes : données isolées, règles et quotas par client | 5114 |
| 16 | `16-BFF` | Backend for Frontend | Un backend par application cliente (site, mobile) : agrégation, réponses taillées pour l'écran, jeton gardé côté serveur | 5115 (web), 5116 (mobile) |
| 17 | `17-CellBased` | Architecture cellulaire | Plusieurs copies complètes et indépendantes du système, chacune pour une partie des clients : rayon d'impact borné | 5117 (routeur) |
| 18 | `18-ModeleActeurs` | Modèle d'acteurs | Un acteur par compte et par virement : état privé, messages traités un par un, aucun verrou ; un acteur qui plante est remplacé | 5118 |
| 19 | `19-SpaceBased` | Architecture spatiale (space-based) | Toutes les données en mémoire, répliquées entre des unités de traitement ajoutées selon la charge ; la base hors du chemin des requêtes | 5119 |
| 20 | `20-Pipeline` | Pipeline (pipes & filters) | Des filtres indépendants reliés par des tuyaux bornés, qui travaillent en même temps sur un fichier de virements | 5120 |
| 21 | `21-Choregraphie` | Chorégraphie (événementiel pur) | Le virement de la démo 14 sans orchestrateur : chaque service réagit aux événements des autres | 5121 |
| 22 | `22-StranglerFig` | Strangler Fig (migration progressive) | Une façade devant l'ancien système ; ses fonctions passent au nouveau une route à la fois, réversibles, jusqu'à pouvoir l'éteindre | 5122 (façade) |
| 23 | `23-RestGrpcGraphQL` | REST, gRPC, GraphQL (styles d'API) | Le même métier exposé en ressources, en procédures et en graphe interrogé par le client : requêtes, octets, erreurs, flux et évolution comparés | 5123 (REST, GraphQL), 5250 (gRPC) |

Données de démo, identiques partout :
- **Alice** `11111111-1111-1111-1111-111111111111` : 1 000 € (5 000 € dans la démo 4, pour tester le plafond)
- **Bob** `22222222-2222-2222-2222-222222222222` : 500 €

Dans la démo 15, Alice et Bob existent chez chacune des trois banques clientes, avec des soldes différents.
Dans la démo 16, ce sont des utilisateurs (`alice` / `bob`, mot de passe `demo`) ; Alice a aussi un Livret A
`44444444-4444-4444-4444-444444444444` (8 000 €).
Dans la démo 22, l'ancien système identifie les comptes par un numéro : Alice `10001`, Bob `10002` (mêmes soldes).
Dans la démo 23, les clients sont `alice` et `bob` ; Alice a trois comptes (courant `10001`, Livret A `10002`, compte
joint `10003`) avec leurs opérations de septembre, Bob un compte courant `20001`.

Tout tourne en mémoire (EF Core InMemory ou dictionnaires) : aucune base à installer.

## Lancer

```powershell
# Tout compiler et tester (438 tests)
dotnet test ArchitecturesDemo.slnx

# Lancer une API (chaque dossier a aussi sa propre solution .slnx)
dotnet run --project 01-Hexagonale\Hexa.Adapters.Web
```

Chaque API contient un fichier **`Banque.http`** avec les requêtes prêtes à envoyer (Visual Studio, Rider ou l'extension REST Client de VS Code).

---

## 0. Couches (N-tiers) — `00-Couches`

```
Couches.Presentation   contrôleurs MVC                                  (→ Metier uniquement)
Couches.Metier         ComptesService : toutes les règles, CompteDto    (→ Donnees)
Couches.Donnees        CompteEntite (anémique), BanqueDbContext, IComptesDao + ComptesDao
```

**À observer**
- **Le sens des dépendances** : le métier dépend de la couche données. C'est elle qui définit `IComptesDao` et `CompteEntite`,
  dans son vocabulaire, et le métier s'y adapte. Comparer avec `02-Onion`, où les flèches sont inversées : le domaine définit
  `IComptesRepository` et l'infrastructure l'implémente. C'est **la** différence entre couches et architectures centrées sur le domaine.
- **Entité anémique** : `CompteEntite` n'a que des propriétés publiques. Les règles sont dans `ComptesService` (style *transaction script*).
  Rien n'empêche un autre code de faire `compte.Solde -= x` sans vérification (voir le commentaire dans le service).
- **Tests couplés à la persistance** : même le faux DAO de `ComptesServiceTests` manipule des `CompteEntite`. Un changement de schéma casse les tests du métier.
- **Couches strictes** : la présentation ne voit que la couche juste en dessous. `DisableTransitiveProjectReferences` dans
  `Couches.Presentation.csproj` empêche d'utiliser `BanqueDbContext` depuis un contrôleur (erreur CS0234 à la compilation),
  et c'est `AddCoucheMetier()` qui branche lui-même la couche données.
- C'est l'approche la plus simple à comprendre, et elle reste tout à fait valable pour une application CRUD où le métier est mince.

---

## 1. Hexagonale — `01-Hexagonale`

```
Hexa.Hexagone                 ← AUCUNE dépendance
  Domaine/                       Compte, exceptions métier
  Ports/Entrants/                IConsulterCompte, IRetirerArgent, IEffectuerVirement
  Ports/Sortants/                IComptes, INotifications
  Application/                   implémentations des ports entrants
Hexa.Adapters.Persistance     ← 2 adapters du MÊME port : ComptesEnMemoire, ComptesFichierJson
Hexa.Adapters.Notifications   ← NotificationsJournal
Hexa.Adapters.Web             ← adapter entrant HTTP + composition root
Hexa.Adapters.Cli             ← 2e adapter entrant : la ligne de commande
```

**À observer**
- Un adapter se remplace par **configuration**, sans toucher au métier :
  `dotnet run --project Hexa.Adapters.Web -- --Persistance=Json`
- Deux portes d'entrée sur le même hexagone. Lancées depuis `01-Hexagonale\`, l'API (en mode Json) et la CLI partagent `comptes.json` :
  ```powershell
  dotnet run --project Hexa.Adapters.Cli -- solde 11111111-1111-1111-1111-111111111111
  dotnet run --project Hexa.Adapters.Cli -- virer 11111111-1111-1111-1111-111111111111 22222222-2222-2222-2222-222222222222 50
  ```
- `Hexa.Tests\Adapters\ContratIComptesTests.cs` : **tests de contrat**. Les mêmes tests tournent sur chaque adapter du port, ce qui garantit qu'ils sont interchangeables.
- La CLI assemble tout à la main (`new RetirerArgent(comptes, notifications)`) : l'hexagone n'a pas besoin d'un conteneur d'injection.

## 2. Onion — `02-Onion`

```
Onion.Domain          Model/ (Compte)  →  Services/ (ServiceVirement)  →  Interfaces/ (IComptesRepository, IUnitOfWork)
Onion.Application     ComptesAppService, VirementsAppService, DTO           (→ Domain)
Onion.Infrastructure  BanqueDbContext (EF Core), ComptesRepository          (→ Application)
Onion.Api             composition root, middleware d'erreurs                (→ tout)
```

**À observer**
- `ServiceVirement` est un **service de domaine** : la règle (plafond de 5 000 €, pas de virement vers soi-même) concerne deux comptes et n'appartient à aucun des deux.
- `VirementsAppService` **orchestre** sans décider : il charge, délègue au domaine, puis enregistre une seule fois via `IUnitOfWork` (tout ou rien).
- Le mapping EF Core est dans `BanqueDbContext.OnModelCreating` : le domaine ne porte aucun attribut EF.
- Les erreurs métier sont traduites en HTTP **une seule fois**, par un middleware.

## 3. Clean Architecture — `03-Clean`

```
Clean.Entites               Compte                                                         (cercle 1)
Clean.CasUtilisation        par cas : Demande/Reponse, IxxxEntree, IxxxSortie, Interacteur  (cercle 2)
Clean.AdaptateursInterface  Controleurs/, Presenters/, Passerelles/                         (cercle 3)
Clean.Web                   ASP.NET Core : simple tuyauterie                                (cercle 4)
```

**À observer**
- L'interacteur **ne renvoie rien** : il appelle `sortie.Succes(...)`, `sortie.Refus(...)` ou `sortie.CompteIntrouvable(...)`.
- **Deux presenters pour le même interacteur** : `GET /comptes/{id}` donne du JSON, `GET /comptes/{id}?format=texte` un relevé texte. L'interacteur n'a pas été modifié pour cela.
- Les adaptateurs ne dépendent pas d'ASP.NET Core (vérifié par `ArchitectureTests`) : les presenters produisent un `VueHttp`, que `Clean.Web` écrit dans la réponse.
- C'est la variante la plus « cérémonieuse » : comparez le nombre de types avec la démo 5 pour le même service rendu.

## 4. Functional Core / Imperative Shell — `04-CoeurFonctionnel`

```
Fonctionnel.Coeur     Compte (record immuable), DecisionRetrait/DecisionVirement, Regles (fonctions pures)
Fonctionnel.Coquille  API + EF Core + horloge + journal : LIRE → DÉCIDER → APPLIQUER
```

**À observer**
- `Regles.Virer(source, cible, montant, dejaVireAujourdhui)` : le cumul du jour (qui dépend de la base et de l'heure) est **calculé par la coquille et passé en paramètre**. Le cœur n'appelle jamais `DateTime.Now`.
- Les tests (`ReglesTests`) n'ont **aucun mock** : des valeurs en entrée, une décision en sortie.
- Dans `Endpoints.cs`, chaque endpoint suit le même schéma en trois temps, commenté.
- Pour tester le plafond journalier de 2 000 € : un virement de 1 500 € passe, puis un de 1 000 € est refusé.

## 5. Vertical Slice — `05-VerticalSlice`

```
Vertical.Api
  Features/Comptes/OuvrirCompte.cs       ← transaction script : tout est dans le fichier
  Features/Comptes/ConsulterCompte.cs    ← lecture : projection LINQ directe, AsNoTracking
  Features/Comptes/RetirerArgent.cs      ← écriture : validation + règle + persistance
  Features/Virements/EffectuerVirement.cs
  Domain/Compte.cs                       ← seulement la règle PARTAGÉE (solde suffisant)
  Data/BanqueDbContext.cs                ← pas de repository par-dessus
```

**À observer**
- Pour comprendre « retirer de l'argent », il suffit d'ouvrir **un seul fichier**.
- Chaque tranche choisit sa technique : la lecture projette directement vers la réponse, l'écriture passe par l'entité.
- `Compte.Retirer` n'a été extrait dans `Domain/` que parce que deux tranches en ont besoin (voir le commentaire de la classe).
- Les tests sont des **tests d'intégration par tranche** (`WebApplicationFactory`), rangés comme le code.

## 6. Composants — `06-Composants`

```
Composants.Comptes     public : IComptesComposant, CompteDto, Resultat, AddComposantComptes()
                       internal : Compte, ComptesDbContext, ComptesComposant
Composants.Virements   public : IVirementsComposant, VirementDto, AddComposantVirements()
                       internal : Virement, VirementsDbContext, VirementsComposant   (→ IComptesComposant)
Composants.Api         ne voit que les interfaces publiques
```

**À observer**
- Dans `Composants.Api\Program.cs`, essayez d'écrire `new Compte(...)` ou d'injecter `ComptesDbContext` : **ça ne compile pas**. L'architecture est imposée par le compilateur.
- Chaque composant a **sa propre base**. Virements ne lit jamais les tables de Comptes : il passe par `IComptesComposant`.
- Faute de transaction commune entre les deux bases, `VirementsComposant` **compense** si le dépôt échoue. Le test `Si_le_depot_echoue_le_retrait_est_compense` le vérifie avec un faux composant Comptes.
- `ArchitectureTests` verrouille la surface publique : rendre un type interne public fait échouer le test.
- `InternalsVisibleTo` (dans le `.csproj`) ouvre l'intérieur au seul projet de tests.

## 7. Monolithe modulaire — `07-MonolitheModulaire`

```
Modulaire.Partage               IModule, bus d'événements en mémoire        (aucun métier)
Modulaire.Comptes.Contrats      IComptesApi, CompteInfo, événement RetraitEffectue
Modulaire.Comptes               ComptesModule (seul type public) + base, API HTTP, domaine   (→ ses Contrats)
Modulaire.Virements.Contrats    événement VirementEffectue
Modulaire.Virements             VirementsModule + base, API HTTP              (→ Comptes.Contrats, Virements.Contrats)
Modulaire.Notifications         NotificationsModule : n'écoute que des événements   (→ Contrats uniquement)
Modulaire.Hote                  assemble les modules : un seul processus, un seul déploiement
```

C'est l'étape d'après les composants (démo 6). Un module n'est plus seulement « du métier derrière une interface » :
c'est une **mini-application** qui possède ses routes HTTP, sa base, ses abonnements, et qu'on branche ou débranche d'une ligne.

**À observer**
- `Modulaire.Hote\Program.cs` : l'hôte ne connaît que `IModule`. Retirer `new NotificationsModule()` de la liste retire la fonctionnalité
  entière (routes, base, abonnements) sans casser les autres.
- **Projets `Contrats`** : Virements a besoin des comptes, mais ne référence que `Modulaire.Comptes.Contrats`, jamais `Modulaire.Comptes`.
  L'implémentation de `IComptesApi` lui est fournie par l'hôte, via l'injection de dépendances.
- **Deux modes de communication** :
  - *synchrone* : Virements appelle `IComptesApi.Debiter/Crediter`, car il a besoin de la réponse tout de suite ;
  - *asynchrone* : Comptes publie `RetraitEffectue` et Virements publie `VirementEffectue`, sans savoir qui écoute.
- **Notifications** a été ajouté sans toucher à Comptes ni à Virements : il s'abonne aux événements et tient sa propre base.
  L'événement transporte les noms des titulaires, pour que l'abonné n'ait pas à rappeler Comptes.
- **Une base par module** (`Banque-Comptes`, `Banque-Virements`, `Banque-Notifications`). Sans transaction commune, Virements
  **compense** le débit si le crédit échoue.
- **Isolation** (`IsolationTests`) : un module se teste en remplaçant un autre par un faux via son contrat. Une panne chez un abonné
  ne fait pas échouer le module qui publie.
- `ArchitectureTests` vérifie les règles : un module ne référence que des `Contrats` et `Partage`, n'expose que sa classe `IModule`,
  et les contrats ne dépendent d'aucun module.
- Le bus est volontairement simple : synchrone et dans le même processus. En production, on utiliserait une *outbox*
  (événement enregistré dans la même transaction que les données) et un traitement en arrière-plan.
- Chaque module peut avoir son architecture interne (tranches verticales, hexagonale…). Le jour où un module doit être déployé
  à part, ses contrats deviennent une API réseau et son bus un vrai broker : c'est le chemin vers les microservices.

## 8. Microservices — `08-Microservices`

```
Micro.Passerelle     :5200  point d'entrée unique des clients (reverse proxy) ; n'expose pas les API internes
Micro.Comptes        :5201  sa base ; API publique + API interne idempotente (/interne/…) ; outbox
Micro.Virements      :5202  sa base ; appelle Comptes par HTTP (retry, timeout, circuit breaker) ; saga + compensation ; outbox
Micro.Notifications  :5203  sa base ; reçoit des messages, ignore les doublons
Micro.Courtier       :5209  mini courtier de messages : livraison « au moins une fois », réessais, lettres mortes
Micro.Lanceur               outil de démo : démarre les services comme le ferait un orchestrateur
```

```powershell
dotnet build 08-Microservices\Microservices.slnx
dotnet run --project 08-Microservices\Micro.Lanceur                          # tous les services
dotnet run --project 08-Microservices\Micro.Lanceur -- comptes passerelle    # seulement certains
```

C'est la démo 7 coupée en processus séparés. Tout ce qui était gratuit dans le monolithe (appel de méthode, transaction,
événement en mémoire) passe maintenant par le **réseau**, qui peut échouer. L'essentiel du code sert à gérer ces échecs.

**À observer**
- **Aucune référence entre services**, ni projet partagé. Le contrat, c'est le JSON : `Micro.Virements\ClientComptes.cs` et
  `Micro.Notifications\Donnees.cs` ont leurs propres copies des messages. Le relais d'outbox est volontairement dupliqué.
- **Résilience des appels** (`Micro.Virements\Program.cs`) : `AddStandardResilienceHandler` ajoute retry, timeout et circuit breaker.
- **Idempotence** :
  - les débits/crédits portent un `operationId` (`Micro.Comptes\Endpoints.cs`). Un retry après une réponse perdue ne débite
    pas deux fois (test `Une_panne_passagere_est_absorbee_par_le_retry_sans_double_debit`) ;
  - Notifications ignore un `Message-Id` déjà traité, car le courtier peut livrer deux fois.
- **Outbox** : le message est enregistré dans la même transaction que la donnée, puis un relais l'envoie au courtier.
  Si le courtier est à terre, le retrait réussit quand même et l'événement part plus tard (test `Si_le_courtier_est_en_panne…`).
- **Saga et compensation** (`Micro.Virements\Endpoints.cs`) : le virement est enregistré avant les appels, avec un statut
  (`EnCours`, `Effectue`, `Refuse`, `Annule`, `AReprendre`). Si le crédit échoue, on recrédite la source.
- **Cohérence à terme**, à tester soi-même :
  1. `dotnet run --project Micro.Lanceur -- courtier comptes virements passerelle` (sans Notifications) ;
  2. faire un virement : il réussit, `/notifications/…` répond 502 et `http://localhost:5209/etat` montre les messages en attente ;
  3. dans un 2e terminal, `dotnet run --project Micro.Lanceur -- notifications` : quelques secondes plus tard, les notifications apparaissent.
- **Passerelle** : `GET /sante` agrège la santé de tous les services. `/interne/…` répond 404, car seules les routes publiques sont relayées.
- **Tests** : chaque service est testé **seul**, avec de faux serveurs HTTP à la place des autres (`Micro.Tests\Outils.cs`).
  Le parcours complet se vérifie en lançant le système.
- En production, on remplacerait le courtier par RabbitMQ / Azure Service Bus, la passerelle par YARP, le lanceur par
  docker compose / Kubernetes / .NET Aspire, et on ajouterait la traçabilité distribuée (OpenTelemetry).

## 9. CQRS — `09-CQRS`

```
Cqrs.Evenements   CompteOuvert, ArgentRetire, VirementEffectue : le SEUL lien entre les deux côtés
Cqrs.Ecriture     Commandes/ (commande + validateur + gestionnaire), BusCommandes (pipeline), Domaine/Compte (agrégat),
                  EcritureDb : état des comptes + journal d'événements, dans la même transaction
Cqrs.Lecture      Modele/ (ReleveCompte, LigneOperation, Statistiques : tables dénormalisées), Projections/Projecteur,
                  Requetes/ (requête + gestionnaire), BusRequetes ; LectureDb : base séparée
Cqrs.Api          POST → commandes, GET → requêtes ; SynchroniseurProjections (tâche de fond)
```

**CQRS** (*Command Query Responsibility Segregation*) part d'un constat : écrire et lire n'ont pas les mêmes besoins.
L'écriture veut des règles et de la cohérence ; la lecture veut des données prêtes à afficher, vite, sous des formes variées.
On sépare donc les deux modèles.

**À observer**
- **Commandes** (`Cqrs.Ecriture\Commandes\`) : un fichier par intention, à l'impératif (`OuvrirCompte`, `RetirerArgent`…).
  Elles ne renvoient **pas de données** : l'API répond `202 Accepted`. L'identifiant du nouveau compte est choisi par l'appelant.
- **Pipeline** (`BusCommandes`) : la validation de forme (`IValidateur<T>`) s'exécute avant tout gestionnaire et rassemble
  toutes les erreurs. Les règles métier (solde suffisant) restent dans l'agrégat. C'est le rôle qu'on confie souvent à MediatR.
- **Agrégat** (`Domaine\Compte.cs`) : il n'a **aucune** propriété pour l'affichage (pas d'historique, pas de statistiques).
  Il note ce qui lui arrive sous forme d'événements, enregistrés avec son état dans la même transaction.
- **Modèle de lecture** (`Cqrs.Lecture\Modele\`) : une table par écran, avec des données dupliquées exprès (titulaire,
  solde après chaque opération, libellés déjà rédigés, totaux maintenus au fil de l'eau). Les requêtes ne font que lire.
- **Projections** (`Projecteur.cs`) : seul code qui écrit côté lecture. Le point de reprise rend la projection **idempotente**
  (rejouer un événement est sans effet). On pourrait jeter toute la base de lecture et la reconstruire depuis les événements.
- **Cohérence à terme, rendue visible** : `appsettings.json` ralentit la projection de 2 s (`Projections:DelaiMs`). Juste après
  un retrait, `GET /comptes/{id}` montre encore l'ancien solde, et `GET /admin/projections` indique le retard. Mettre 0 pour
  une projection quasi immédiate.
- **Deux côtés indépendants** : ni `Cqrs.Ecriture` ni `Cqrs.Lecture` ne se référencent (vérifié par `ArchitectureTests`).
  Les tests du côté lecture n'utilisent que des événements, sans aucune commande.
- **À ne pas confondre** :
  - CQRS n'impose pas deux bases. On peut séparer seulement les modèles, sur une même base, et lire en synchrone.
    C'est la variante la plus simple, souvent suffisante. Les deux bases et la cohérence à terme se justifient quand
    les lectures sont très nombreuses ou très différentes des écritures.
  - CQRS n'est pas l'*Event Sourcing*. Ici, l'état des comptes est stocké normalement et les événements servent à alimenter
    la lecture. En Event Sourcing, les événements **sont** la source de vérité : l'état se reconstruit en les rejouant.
    C'est la démo 10.

## 10. Event Sourcing — `10-EventSourcing`

```
Es.Domaine       Evenements (CompteOuvert, ArgentDepose, ArgentRetire v2, VirementEmis/Recu, CompteCloture),
                 Compte : agrégat reconstitué en rejouant ses événements
Es.Stockage      MagasinEvenements (ajout seul, concurrence optimiste, fichier .jsonl), SerialiseurEvenements (+ upcasting),
                 DepotComptes (chargement, instantanés, voyage dans le temps)
Es.Projections   ProjectionReleves, ProjectionPlusGrosRetraits, MoteurProjections (suivi du flux global, reconstruction)
Es.Api           commandes, audit, voyage dans le temps, administration des projections
```

Dans toutes les autres démos, on stocke l'**état** (le solde) et on oublie comment on y est arrivé. Ici, on stocke la
**suite des événements** et on n'en efface jamais aucun. L'état n'est qu'un calcul : le résultat du rejeu de l'historique.

**À observer**
- **La base de données est un fichier lisible** : `Es.Api\evenements.jsonl`, une ligne par événement, en ajout seul. Arrêtez
  l'API, ouvrez le fichier, relancez : tout l'état (soldes, projections) est reconstruit à partir de ces lignes.
  Pour repartir de zéro, supprimez le fichier.
- **Décider / appliquer** (`Es.Domaine\Compte.cs`) : les décisions (`Retirer`, `Cloturer`…) vérifient les règles et
  enregistrent un événement ; `Appliquer` fait évoluer l'état **sans aucune règle**, car un fait passé ne se refuse pas.
- **Tests en Given / When / Then** (`CompteTests`) : *étant donné* ces événements, *quand* je retire 30 €, *alors* j'attends
  `ArgentRetire(30)`. On teste le comportement sans base, sans mock, avec des faits.
- **Concurrence optimiste** : chaque flux a une version. `If-Match: 1` sur une commande signifie « je décide d'après la version 1 ».
  Si le compte a bougé depuis : 409, rien n'est écrit. Le virement écrit dans deux flux en tout ou rien.
- **Audit et voyage dans le temps** : `GET /comptes/{id}/evenements` donne l'historique complet ;
  `GET /comptes/{id}/etat?version=2` (ou `?date=…`) recalcule l'état du compte tel qu'il était à ce moment-là.
- **Projections jetables** : `POST /admin/projections/reconstruire` efface toutes les vues et rejoue tout le magasin.
  `ProjectionPlusGrosRetraits` illustre l'atout majeur de l'approche : une vue ajoutée **après coup** connaît d'emblée tout
  l'historique, ce qui est impossible quand on n'a gardé que l'état final.
- **Instantanés** (`DepotComptes`) : tous les 5 événements, une photo de l'état est mise en cache. Charger un compte ne rejoue
  alors que la suite (test `Un_instantane_evite_de_rejouer…`). Ce n'est qu'un cache : on peut l'effacer sans rien perdre.
- **Évolution des événements (upcasting)** : `ArgentRetire` a gagné un champ `Motif` en v2. Les anciens événements v1 ne sont
  jamais réécrits ; `SerialiseurEvenements` les convertit à la lecture (motif « Non précisé »).
- **Les contreparties** : on ne peut ni modifier ni supprimer un événement. Les erreurs se corrigent par un nouvel événement
  compensatoire, et le RGPD oblige à prévoir l'effacement (chiffrement des données personnelles, puis destruction de la clé).
  Il faut aussi versionner les événements et garder des projections à jour. C'est puissant pour les domaines où l'historique
  a de la valeur (banque, comptabilité, logistique, audit), et souvent excessif pour un CRUD.
- En production : EventStoreDB, Marten (PostgreSQL) ou Axon remplacent le magasin écrit ici à la main.

## 11. Domain-Driven Design — `11-DDD`

```
Ddd.TenueDeCompte.Domaine          LANGAGE.md (glossaire), ObjetsValeurs/ (Argent, Devise, Iban, NomTitulaire, CompteId),
                                   Comptes/ (agrégat Compte + entité Mouvement, FabriqueDeComptes, IComptes, ServiceDeVirement)
Ddd.TenueDeCompte.Application      ServiceComptes (cas d'utilisation), PublicateurEvenements (domaine → langage publié)
Ddd.TenueDeCompte.Infrastructure   EF Core (mapping des objets-valeurs), couche anti-corruption vers le système de change hérité
Ddd.TenueDeCompte.Contrats         LANGAGE PUBLIÉ : VirementEmis, IAbonneTenueDeCompte, ITenueDeCompteApi (service hôte ouvert)
Ddd.Conformite                     2e contexte délimité : LANGAGE.md, DossierDeVigilance, Alerte, SurveillanceDesVirements
Ddd.Api                            héberge les deux contextes
```

Le DDD n'est pas une organisation de dossiers comme les démos précédentes. C'est une **façon de concevoir** :
partir du métier, parler sa langue, et laisser le modèle guider le code. On peut l'appliquer avec une architecture
hexagonale, Onion ou Clean (ici, l'Onion de la démo 2). Il a deux volets.

**Le volet tactique : bien modéliser à l'intérieur d'un contexte**
- **Langage omniprésent** : `LANGAGE.md` liste les termes du métier ; on retrouve chacun tel quel dans le code
  (`PlafondRetraitJournalier`, `Titulaire`, `Virement`…).
- **Objets-valeurs** (`ObjetsValeurs\`) : définis par leur valeur, immuables, **toujours valides**. Un `Iban` vérifie sa clé
  modulo 97 à la création, donc recevoir un `Iban` suffit à savoir qu'il est valide. `Argent` porte sa devise et refuse
  d'additionner des euros et des dollars. `CompteId` évite de confondre deux `Guid`. Comparez avec les `decimal` et `string`
  nus des autres démos (« primitive obsession »).
- **Agrégat** (`Compte`) : une racine qui garantit ses invariants. Sa **frontière** est dictée par une règle : le plafond de
  retrait journalier oblige les `Mouvement`s à faire partie de l'agrégat. On ne charge et on n'enregistre jamais un mouvement seul.
- **Fabrique** (`FabriqueDeComptes`) : l'ouverture d'un compte (numéro, calcul de l'IBAN, plafond) est une logique de création
  qui n'appartient ni au compte ni à l'application.
- **Service de domaine** (`ServiceDeVirement`) : le virement touche deux agrégats et un taux de change. Il vit dans un service
  sans état, toujours dans le langage du domaine.
- **Dépôt** (`IComptes`) : un par racine d'agrégat, défini dans le domaine, implémenté dans l'infrastructure.
- **Événements de domaine** : levés par l'agrégat, publiés après l'enregistrement.

**Le volet stratégique : découper en contextes et organiser leurs relations**
- **Deux contextes délimités**, deux modèles pour la même réalité. Pour la *Tenue de compte*, un compte a un solde, un plafond
  et des mouvements. Pour la *Conformité*, c'est un `DossierDeVigilance` avec des virements observés **en euros** et des
  alertes, sans solde (voir les deux `LANGAGE.md`). Vouloir un seul modèle « Compte » pour tout le monde produirait un modèle obèse.
- **Langage publié** (`Ddd.TenueDeCompte.Contrats`) : les autres contextes ne voient que des événements d'intégration en
  types simples. `PublicateurEvenements` traduit les événements de domaine (riches, internes) en événements d'intégration.
  La Conformité ne référence que ce projet (vérifié par un test d'architecture).
- **Service hôte ouvert** (`ITenueDeCompteApi`) : ce que la Tenue de compte accepte de faire pour les autres. Au 3e virement
  en 10 minutes, la Conformité soupçonne un fractionnement et **demande** le blocage du compte : elle ne le bloque pas elle-même.
- **Couche anti-corruption** (`CoucheAntiCorruption.cs`) : le système de change hérité renvoie `"EURUSD=10832;EURGBP=8571;…"`
  (taux × 10 000, depuis l'euro seulement, avec des devises que la banque ne tient pas). `AdaptateurTauxDeChange` traduit
  ce format vers `Argent`/`Devise`, calcule les taux croisés (USD → CHF) et écarte GBP et JPY. Rien de ce format ne fuit
  dans le domaine.
- **Carte des contextes** de cette démo :
  ```
  [Système de change hérité] ──(couche anti-corruption)──▶ [Tenue de compte] ──(langage publié + service hôte ouvert)──▶ [Conformité]
                                                               ▲                                                            │
                                                               └──────────── demande de blocage (ITenueDeCompteApi) ───────┘
  ```
- Quand l'utiliser : quand le métier est **complexe** et que des experts sont disponibles pour construire le langage avec vous.
  Pour un CRUD, c'est beaucoup de cérémonie pour peu de gain.

## 12. Serverless — `12-Serverless`

```
Sls.Plateforme   LE FOURNISSEUR CLOUD SIMULÉ (on ne l'écrit jamais en vrai) : MoteurExecution (instances, démarrage à froid,
                 mise à l'échelle, recyclage, étranglement, délai max, facturation), services gérés (TableGeree avec ETag,
                 ServiceFiles avec nouveaux essais et file poison, ServiceStockage, réglages, journal), console /plateforme
Sls.Fonctions    LE CODE QU'ON ÉCRIT : Deploiement.cs (manifeste) + des fonctions statiques sans état
Sls.Hote         fait tourner la plateforme en local (l'équivalent de « func start » ou « sam local »)
```

En serverless (*Function as a Service* : AWS Lambda, Azure Functions…), on ne déploie plus une application qui tourne en
continu, mais des **fonctions** que le fournisseur exécute à la demande, quand un **événement** survient (requête HTTP,
message en file, fichier déposé, horloge). Pas de serveur à gérer, et on ne paie que l'exécution.

La plateforme est simulée (aucun téléchargement), mais elle reproduit les comportements qui changent la façon de coder.
`Sls.Fonctions` ne contient que ce qu'on écrirait réellement.

**À observer** (requêtes prêtes dans `Sls.Hote\Banque.http`, en-têtes `X-Instance`, `X-Demarrage-A-Froid`, `X-Duree-Ms`)
- **Pas de serveur à écrire** : `Deploiement.cs` déclare les fonctions et leurs déclencheurs (HTTP, file, stockage, minuterie),
  avec la mémoire allouée. Chaque fonction est une méthode statique `(déclencheur, contexte) → réponse`.
- **Démarrage à froid** : le premier appel d'une fonction crée une instance (≈ 600 ms ici) ; les suivants réutilisent
  l'instance « chaude » (quelques ms). Après 20 s sans appel, l'instance est recyclée : **mise à zéro**, plus rien ne tourne.
- **Mise à l'échelle automatique** : une instance ne traite qu'un appel à la fois. Déposer un fichier de 8 virements
  (`POST /imports`) déclenche `TraiterImport`, qui crée 8 messages (*fan-out*) ; `ExecuterVirement` monte alors à
  8 instances en parallèle (`GET /plateforme/fonctions` → `picInstances`). Au-delà du maximum : étranglement (429).
- **Sans état** : `GET /visites` compare un compteur gardé dans la mémoire de l'instance (faux dès qu'il y a plusieurs
  instances, remis à zéro au recyclage) et un compteur dans la table gérée (juste). Tout ce qui compte va dans les services gérés.
- **Concurrence** : des dizaines d'instances peuvent modifier le même compte. Les écritures sont conditionnelles (ETag) :
  30 retraits simultanés de 100 € sur un compte de 1 000 € → exactement 10 réussissent (test `Des_retraits_simultanes…`).
- **Asynchrone par défaut** : `POST /virements` répond `202` tout de suite ; le virement est exécuté par une autre fonction,
  déclenchée par la file, qui envoie ensuite des notifications dans une 2e file.
- **Nouveaux essais et messages empoisonnés** : une exception fait relivrer le message (attente croissante). Après 3 échecs,
  il part dans `notifications-poison`. Scénario : `PUT /plateforme/reglages/Sms:EnPanne` à `true`, faire un virement,
  constater les messages empoisonnés, remettre à `false`, puis `POST /plateforme/files/notifications-poison/rejouer`.
- **Idempotence obligatoire** : un message peut être livré deux fois. `Compte.Appliquer` ignore une opération déjà connue,
  les notifications ont une clé déterministe, et un fichier d'import retraité ne crée pas de doublons.
- **Délai maximal** : une fonction qui dépasse son délai est arrêtée et son instance détruite (504).
- **Facturation** : `GET /plateforme/facture` donne invocations × prix + durée × mémoire (Go-s), aux tarifs d'AWS Lambda.
  Une démo complète coûte quelques millièmes de centime ; mais un trafic constant et élevé peut coûter plus cher qu'un serveur.
- **Tests** : les fonctions sont de simples méthodes, testées directement avec `ContexteFonction.PourTests` (sans plateforme) ;
  la plateforme et le système complet ont leurs propres tests.
- Pour passer au réel : Azure Functions (isolated worker) ou AWS Lambda, avec Azurite / LocalStack en local, et Durable
  Functions / Step Functions pour orchestrer des enchaînements longs.

---

## 13. Microkernel — `13-Microkernel`

```
Mk.Contrats      le SDK des plugins : IPlugin, IControleOperation, IRegleFrais, IFormatReleve
Mk.Noyau         NoyauBancaire (comptes, débit, crédit), RegistrePlugins, ChargeurPlugins (AssemblyLoadContext)
Mk.Hote          API + surveillance du dossier plugins/ (rechargement automatique)
Mk.Plugins.*     PlafondNocturne, ListeNoire, FraisVirement, Exports (CSV + QIF) → compilés dans plugins/{Nom}/
```

Un noyau minimal qui ne référence **aucun** plugin ; tout ce qui varie est branché à des points d'extension.

**À observer** (détails : [Docs/13-microkernel.md](Docs/13-microkernel.md))
- `GET /admin/plugins` : 5 extensions dans 4 DLL, avec leur état et leurs points d'extension.
- **Rechargement à chaud** : modifier `plugins\Mk.Plugins.ListeNoire\plugin.json`, ou recompiler un plugin **pendant que
  l'API tourne** (`dotnet build Mk.Plugins.ListeNoire`) : il est rechargé automatiquement. Les DLL sont lues en mémoire,
  donc jamais verrouillées ; les anciens contextes de chargement sont déchargés.
- `POST /admin/plugins/{nom}/desactiver` / `activer` : changer le comportement sans redémarrer.
- **Le piège classique**, évité : `Mk.Contrats` n'est **pas** copié dans le dossier des plugins (`Private="false"`) ; sinon
  l'`IPlugin` du plugin serait un autre type que celui du noyau. Un test vérifie qu'il n'y en a qu'un exemplaire.
- Un plugin qui plante est mis « en erreur » et écarté (l'opération en cours est refusée par sécurité) ; un plugin qui
  exige un noyau plus récent est refusé.

## 14. Saga orchestrée — `14-SagaOrchestree`

```
Saga.Participants   services simulés et idempotents : Comptes (réserver / capturer), Conformité, Change, Notifications
Saga.Orchestrateur  DefinitionVirement (les 6 étapes et leurs compensations), MoteurSaga, EtatSaga, MagasinSagas
Saga.Api            démarrer / suivre une saga, décision de l'analyste, pannes simulées (PUT /chaos), arrêt / relance
```

Les démos 7 et 8 coordonnent par **chorégraphie** (chacun réagit aux événements des autres) ; ici, un **orchestrateur**
explicite conduit le processus : ① réserver les fonds, ② contrôle de conformité, ③ change, ④ crédit, ⑤ débit définitif
(point de non-retour), ⑥ notification.

**À observer** (détails : [Docs/14-saga-orchestree.md](Docs/14-saga-orchestree.md), requêtes dans `Saga.Api\Banque.http`)
- `GET /sagas/{id}` : l'état de chaque étape et le journal complet de la saga.
- **Panne passagère** : `PUT /chaos {"echecsChangeRestants":2}` puis un virement Alice → Carla (USD) : 2 nouveaux essais, puis succès.
- **Panne durable** : `{"changeHorsService":true}` : abandon au 4e échec, puis compensation en ordre inverse (fonds libérés).
- **Attente d'un événement extérieur** : un virement ≥ 10 000 € attend la décision d'un analyste
  (`POST /sagas/{id}/conformite`) ; sans décision avant l'échéance (60 s), il est compensé.
- **Point de non-retour** : `{"notificationsHorsService":true}` : après le débit définitif on ne compense plus, la saga
  passe en `ATraiterManuellement`.
- **Reprise** : `POST /admin/orchestrateur/arreter` pendant une saga, puis `relancer` : elle reprend exactement où elle était,
  car tout son état est dans le magasin.
- En production : Temporal, Azure Durable Functions, AWS Step Functions, ou les sagas de MassTransit / NServiceBus.

## 15. Multi-tenant — `15-MultiTenant`

```
Mt.Locataires   la LOCATION, sans métier : Locataire (formule, isolation, règles, quotas), CatalogueLocataires,
                ContexteLocataire, résolveurs (sous-domaine, en-tête), MiddlewareLocataire, limitation de débit par locataire
Mt.Banque       le métier, écrit comme pour une seule banque : ServiceBancaire ;
                BanqueDbContext (filtre global, estampillage, refus des écritures croisées), FabriqueBanque (base partagée ou dédiée)
Mt.Api          API des clients + back-office de la plateforme (/plateforme : inscription, formule, suspension, départ, tâches)
```

Une **seule application, un seul déploiement**, pour plusieurs clients (les *locataires*, ici trois banques) qui ne doivent
jamais voir les données les uns des autres. C'est le modèle des SaaS. La question n'est plus « comment organiser le code »,
mais « comment partager l'application **sans mélanger** les clients ».

**À observer** (détails : [Docs/15-multi-tenant.md](Docs/15-multi-tenant.md), requêtes dans `Mt.Api\Banque.http`)
- **Le métier ignore le locataire** : `ServiceBancaire` ne contient aucun `where LocataireId = …`. Le locataire est identifié
  une fois, par `MiddlewareLocataire` (sous-domaine `beta.localhost:5114` ou en-tête `X-Locataire`), puis lu dans `ContexteLocataire`.
- **La même URL, trois réponses** : `GET /comptes/1111…` donne Alice à 1 000 €, 3 000 € ou 25 000 € selon la banque. La clé
  primaire est composite (`LocataireId`, `Id`) : les mêmes identifiants coexistent dans la base partagée.
- **Le garde-fou est dans le DbContext** : filtre global sur toutes les entités qui implémentent `IAppartientAUnLocataire`,
  estampillage des insertions, exception si une écriture vise un autre locataire. **Sans locataire, toute requête échoue**
  au lieu de tout renvoyer ; la plateforme doit écrire `IgnoreQueryFilters()` pour voir plusieurs clients.
- **Deux modèles d'isolation côte à côte** (`GET /plateforme/stockage`) : Alpha et Bêta (Standard) partagent `banque-partagee`,
  Gamma (Premium) a sa base `banque-gamma`. Seule `FabriqueBanque` connaît cette différence.
- **Changer de formule** : `POST /plateforme/locataires/alpha/formule` (Premium) déménage les données d'Alpha dans une base
  dédiée, le locataire étant suspendu le temps de la copie.
- **Règles et quotas par client** : plafond de virement, frais, nombre de comptes (contrat de chaque banque) ; requêtes par
  minute (formule). Un **voisin bruyant** reçoit 429 sans ralentir les autres : un limiteur par locataire, et non un global.
- **Une tâche de fond n'a pas de locataire** : `POST /plateforme/taches/frais-mensuels` ouvre un scope par banque et y fixe le
  locataire elle-même. L'oublier lève une exception, plutôt que de prélever sur toutes les banques.
- **Arrivée et départ d'un client** sans déploiement : `POST /plateforme/locataires` puis `DELETE` (base supprimée en Premium,
  lignes supprimées en Standard).
- Un compte d'une autre banque répond **404**, comme un compte inexistant : on ne révèle pas son existence.
- En production : le locataire vient du **jeton** de l'utilisateur authentifié (ou est vérifié contre lui), le catalogue est
  une base à part, et les migrations de schéma doivent passer sur **chaque** base dédiée. Bibliothèque courante : Finbuckle.MultiTenant.

## 16. Backend for Frontend (BFF) — `16-BFF`

```
Bff.Services  :5210  les services métier, génériques (simulés dans un seul processus) : identité (jetons), profils, comptes,
                     opérations, conseillers, offres ; latence et pannes réglables (PUT /chaos)
Bff.Web       :5115  le BFF du SITE : sert l'application (wwwroot), session côté serveur + cookie HttpOnly (le jeton ne va
                     jamais au navigateur), protection CSRF, un tableau de bord agrégé en une requête
Bff.Mobile    :5116  le BFF de l'APPLICATION MOBILE : réponses compactes, un appel par écran, pagination par curseur,
                     versions /v1 et /v2 alignées sur les versions de l'application
```

```powershell
dotnet run --project 16-BFF\Bff.Services     # puis, dans deux autres terminaux :
dotnet run --project 16-BFF\Bff.Web          # http://localhost:5115 (alice / demo)
dotnet run --project 16-BFF\Bff.Mobile
```

Des services génériques servent plusieurs applications aux besoins opposés : un site sur grand écran et réseau rapide, une
application mobile sur petit écran et réseau lent. Plutôt que de faire choisir les services entre les deux, chaque application
a **son propre backend**, écrit et déployé par son équipe, qui fait l'interface entre elle et les services.

**À observer** (détails : [Docs/16-bff.md](Docs/16-bff.md), requêtes dans `Bff.Web\Banque.http` et `Bff.Mobile\Banque.http`)
- **Un écran, une requête** : `GET /api/tableau-de-bord` déclenche 7 appels aux services, faits côté serveur et **en parallèle**.
  Les en-têtes `X-Appels-Aval`, `X-Duree-Totale-Ms` (≈ 2 latences) et `X-Duree-Aval-Cumulee-Ms` (≈ 7 latences) le montrent.
- **Le jeton reste sur le serveur** (`Bff.Web\Session.cs`) : le navigateur ne reçoit qu'un cookie `HttpOnly`, `SameSite=Strict`,
  qui désigne une session gardée par le BFF. La page affiche `document.cookie = ""` : un script injecté (XSS) n'a rien à voler.
  Se déconnecter supprime la session, et un cookie copié auparavant ne vaut plus rien.
- **CSRF** : toute requête vers `/bff` et `/api` doit porter `X-CSRF: 1`, ce qu'un formulaire d'un autre site ne peut pas faire.
- **Aucun passe-droit** : le BFF appelle les services avec le jeton **de l'utilisateur** ; Alice ne peut pas débiter le compte
  de Bob, même en passant par le BFF (403 des services).
- **Une réponse par écran** : le mobile reçoit une réponse plus de 5 fois plus légère que le site, sans IBAN ni champ inutile,
  avec des libellés raccourcis (« CB CARREFOUR 0910 PARIS 15 » → « Carrefour »).
- **Dégradation** : si les offres sont en panne ou trop lentes (budget de 800 ms), le tableau de bord s'affiche sans elles
  (`"degrade": ["offres"]`) au lieu d'échouer.
- **Versions** : `/mobile/v2/accueil` ajoute un bandeau d'offre pour la nouvelle version de l'application ; `/v1` reste servie
  aux téléphones pas encore mis à jour. Les services n'ont pas changé.
- **Deux BFF indépendants** : aucun code partagé entre eux ni avec les services (vérifié par un test). Les contrats sont
  recopiés, avec seulement les champs utiles à chaque écran.
- Ne pas confondre avec une **passerelle d'API** (démo 8) : la passerelle est unique et générique (routage, sécurité, quotas) ;
  un BFF est spécifique à une application. Les deux se combinent souvent. En production : Duende BFF ou YARP pour le relais,
  OpenID Connect pour la connexion, un cache distribué pour les sessions.

## 17. Architecture cellulaire (cell-based) — `17-CellBased`

```
Cell.Cellule   L'APPLICATION COMPLÈTE (API, données, règles), identique dans chaque cellule ; chaque instance ne sert que
               ses clients. Lancée en c1 :5231, c2 :5232, c3 :5233 (+ c4 :5234 en réserve)
Cell.Routeur   :5117  plan de DONNÉES : clé de partition (le client) → cellule → relais, rien d'autre
                      plan de CONTRÔLE (/plateforme) : placement, ajout de cellules, migration, déploiement par vagues
Cell.Lanceur          démarre le routeur et les cellules (le même programme, lancé trois fois)
```

```powershell
dotnet build 17-CellBased\CellBased.slnx
dotnet run --project 17-CellBased\Cell.Lanceur            # routeur + c1, c2, c3
dotnet run --project 17-CellBased\Cell.Lanceur -- c4      # plus tard : une 4e cellule, à enregistrer
```

Au lieu d'un seul système qui grossit, on fait tourner **plusieurs copies complètes et indépendantes** de l'application,
les **cellules**, chacune servant une partie des clients. Une panne, un mauvais déploiement ou un client trop gourmand
n'atteint qu'une cellule : c'est le **rayon d'impact** qui est borné. Approche décrite par AWS pour ses propres services,
adoptée notamment par Slack, et proche des « pods » de Salesforce.

**À observer** (détails : [Docs/17-cell-based.md](Docs/17-cell-based.md), requêtes dans `Cell.Routeur\Banque.http`)
- **Le routeur est minuscule** (`Relais.cs`) : il lit le client dans l'URL, trouve sa cellule dans la table de placement,
  relaie, et ajoute `X-Cellule`. Pas de métier, pas de base : c'est le seul composant partagé, il doit être increvable.
- **Rayon d'impact** : arrêter le processus de c2 → Bob et Emma reçoivent 503, Alice, Carla et David ne voient rien.
- **Placement** : un nouveau client va dans la cellule ouverte la moins chargée. Quand toutes sont pleines (3 places chacune),
  on **ajoute** une cellule (`POST /plateforme/cellules`) au lieu d'en agrandir une : toutes gardent une taille connue et testée.
- **Déploiement par vagues** : `POST /plateforme/deploiements` met d'abord à jour **une** cellule (le canari), vérifie sa
  sonde de santé, puis les autres. La version 1.1 (bogue : un retrait débité deux fois) est arrêtée au canari et annulée ;
  l'historique des versions montre que c2 et c3 ne l'ont jamais reçue.
- **Migration** : `POST /plateforme/clients/carla/migrer` déplace un client (données, historique, opérations déjà traitées)
  vers une autre cellule ; il est indisponible un instant (503 + `Retry-After`), les autres non.
- **Le prix : les échanges entre cellules.** Un virement entre deux clients de la même cellule est une transaction locale ;
  entre deux cellules, c'est un débit, puis un crédit idempotent via le routeur, et une compensation si la cellule du
  bénéficiaire est injoignable. D'où l'enjeu du choix de la **clé de partition** : la plupart des opérations doivent
  rester dans une cellule.
- **Plan de contrôle et plan de données** sont séparés : si le plan de contrôle tombe, on ne peut plus placer ni déployer,
  mais le trafic continue avec la table de placement existante.
- Ne pas confondre avec le **multi-tenant** (démo 15), qui partage une instance entre clients ; les cellules sont souvent
  multi-tenant à l'intérieur. Ni avec les **microservices** (démo 8), qui découpent par fonction : une cellule contient
  toutes les fonctions, éventuellement sous forme de microservices.

## 18. Modèle d'acteurs — `18-ModeleActeurs`

```
Acteurs.Noyau    le RUNTIME, écrit pour la démo (≈ 300 lignes, sans package) : Acteur, ReferenceActeur (une adresse),
                 SystemeActeurs (une boîte aux lettres par acteur, activation, supervision, passivation)
Acteurs.Banque   CompteActeur (un par compte), VirementActeur (un par virement), messages immuables,
                 MagasinEtats (stockage durable, une ligne par acteur), Pieges (pannes et lenteurs à la demande)
Acteurs.Api      :5118  une route = un message à un acteur ; /systeme pour observer et provoquer des pannes ; /demo
```

Chaque compte est un **acteur** : un objet qui a son propre état, ne le partage avec personne, et ne reçoit que des
**messages**, traités **un par un** depuis sa boîte aux lettres. Plus besoin de verrou ni de transaction pour protéger
le solde : personne d'autre ne peut y toucher. Le modèle d'Erlang, d'Akka et d'Orleans (Halo, Xbox) ; ici, des acteurs
*virtuels* à la Orleans, activés au premier message et endormis après 30 s d'inactivité.

**À observer** (détails : [Docs/18-modele-acteurs.md](Docs/18-modele-acteurs.md), requêtes dans `Acteurs.Api\Banque.http`)
- **Concurrence sans verrou** : `POST /demo/retraits-simultanes` lance 1 000 retraits de 3 € **en même temps** sur le
  compte d'Alice : exactement 333 acceptés, solde final 1 €, jamais négatif. `CompteActeur` lit, vérifie et modifie son
  solde comme un programme séquentiel.
- **Virements sans interblocage** : un acteur par virement converse avec les deux comptes (débit, crédit, compensation si
  la cible refuse). `POST /demo/virements-croises` lance des virements Alice → Bob et Bob → Alice simultanés : le total
  est conservé. L'identifiant du virement est l'adresse de son acteur : rejouer `facture-42` renvoie la même réponse.
- **« Let it crash »** : `POST /systeme/pannes` fait planter un acteur au pire moment (solde modifié en mémoire, pas
  encore écrit) → `500`, le superviseur le **remplace** par une incarnation neuve qui relit le stockage : le retrait n'a
  pas eu lieu. Plus de 3 pannes par minute → l'acteur est arrêté (`503`) ; les autres comptes ne voient rien.
- **Reprise d'un virement** : planté après le débit, il est noté `DebitFait` ; rejoué, il termine sans débiter deux fois.
- **Activation et passivation** : `GET /systeme` liste les acteurs en mémoire ; `POST /systeme/passivation` les endort,
  leur état reste dans `/systeme/stockage`, et le message suivant ne réveille que l'acteur concerné.
- **Le prix** : pas de `GET /comptes` (il faudrait réveiller et interroger chaque acteur : on ajouterait une vue de
  lecture, cf. CQRS) ; pas de transaction entre acteurs ; deux acteurs qui s'attendent mutuellement s'interbloquent.
- En production : **Orleans**, **Akka.NET** ou **Proto.Actor**, qui répartissent les acteurs sur plusieurs serveurs.

## 19. Architecture spatiale (space-based) — `19-SpaceBased`

```
Espace.Grille    l'INTERGICIEL VIRTUALISÉ, simulé (sans package) : grille de données (une réplique par unité, réplication
                 asynchrone, versions, détection des collisions), grille de messages (affinité / tourniquet), pompe et
                 écrivain de données, lecteur de données, gestionnaire de déploiement (élasticité)
Espace.Banque    le métier d'une unité : il ne lit et n'écrit que dans la réplique de son unité
Espace.Api       :5119  /comptes, /virements ; /grille pour observer et provoquer des pannes ; /demo
```

La base de données est le goulot de toutes les autres démos. Ici, elle sort du chemin des requêtes : chaque **unité de
traitement** a **toutes les données en mémoire**, dans une grille répliquée entre les unités ; une **pompe** écrit dans
la base en arrière-plan. On ajoute des unités quand la charge monte, on les retire quand elle baisse. Le modèle de
GigaSpaces, Hazelcast ou Apache Ignite (billetterie, enchères, trading).

**À observer** (détails : [Docs/19-space-based.md](Docs/19-space-based.md), requêtes dans `Espace.Api\Banque.http`)
- **La base peut tomber** (`POST /grille/base/panne`) : les requêtes répondent toujours `200`, `pompeEnAttente` grossit,
  puis se vide quand la base revient.
- **Les collisions**, le prix de la grille répliquée : `POST /demo/retraits-concurrents` en **affinité** (chaque compte
  toujours sur la même unité) ne perd rien ; au **tourniquet** (`POST /grille/routage`), deux unités écrivent le même
  compte en même temps et des écritures acceptées se perdent (essai réel : 200 retraits de 1 € acceptés, 100 € débités).
- **Élasticité** : `POST /demo/charge` (600 ouvertures simultanées) fait passer de 2 à 6 unités, chacune démarrée par une
  simple copie mémoire d'une autre ; au calme, retour à 2. Le journal (`GET /grille/journal`) raconte.
- **Démarrage à froid** : `POST /grille/panne-totale` vide la grille ; la requête suivante attend que la pompe ait tout
  écrit, puis recharge la grille depuis la base (`503` si la base est aussi en panne).
- `GET /grille/comparaison/{id}` montre une donnée dans chaque réplique et dans la base, et la fenêtre de réplication.
- Même en affinité, un changement du nombre d'unités ouvre une courte fenêtre de collision (clés qui changent d'unité).

## 20. Pipeline (pipes & filters) — `20-Pipeline`

```
Pipeline.Noyau   le MOTEUR, générique (sans package) : IFiltre, étages reliés par des tuyaux bornés (Channels),
                 étages parallèles ordonnés ou non, rejets par motif, mesures par étage, arrêt sur panne
Pipeline.Banque  les filtres de l'import d'un fichier de virements : Analyser, Valider (IBAN), Dédupliquer, Convertir,
                 Contrôler la fraude, Exécuter ; et leur assemblage en deux pipelines (import, import à blanc)
Pipeline.Api     :5120  POST /imports (le corps est le fichier CSV, lu en flux) ; /demo/import-massif
```

Un traitement de données découpé en **filtres** indépendants, reliés par des **tuyaux** : le modèle du shell Unix, des
compilateurs et des ETL. Ici, l'import d'un fichier de virements envoyé par une entreprise ; chaque filtre fait une seule
chose, et tous travaillent en même temps, chacun sur une ligne différente.

**À observer** (détails : [Docs/20-pipeline.md](Docs/20-pipeline.md), requêtes dans `Pipeline.Api\Banque.http`)
- **Les rejets** : le fichier d'exemple (11 lignes) donne 3 virements et 8 rejets, chacun écarté par le filtre qui le
  concerne (ligne mal formée, IBAN invalide, doublon, devise inconnue, liste noire, plafond, solde insuffisant).
- **Recomposition** : `POST /imports?aBlanc=true` est le même pipeline sans le dernier filtre : il dit ce qui passerait.
- **Contre-pression** : `POST /demo/import-massif` importe 100 000 lignes générées à la volée ; jamais plus de quelques
  centaines en mémoire (`maxEnTransit`).
- **Le goulot** : le bilan mesure chaque étage ; avec 1 copie, le contrôle de fraude est occupé à 94 % et les autres
  attendent. Avec 8 copies (`parallelismeFraude`), l'import va 5 fois plus vite, et les lignes ressortent dans l'ordre du
  fichier (l'ordre compte : un virement peut dépendre du précédent).
- **Reprise** : `?panneALaReference=VIR-010` arrête l'import en plein milieu (500) ; rejouer le fichier termine le
  travail sans rien exécuter deux fois (dédoublonnage par référence).

## 21. Chorégraphie (événementiel pur) — `21-Choregraphie`

```
Choreo.Bus       le COURTIER, simulé (sans package) : une file par abonné, livraison « au moins une fois », nouveaux essais,
                 lettres mortes, journal de tous les événements (corrélation + cause)
Choreo.Services  les contrats d'événements, et les services, qui ne se connaissent pas : Comptes, Conformité, Change,
                 Notifications, Fidélité (ajouté après coup), Suivi (la projection qui reconstitue l'état des virements)
Choreo.Api       :5121  POST /virements (202) ; /conformite pour l'analyste ; /bus, /chaos pour observer et casser
```

Le **même virement que la démo 14**, avec les mêmes données et les mêmes règles, mais **sans orchestrateur** : chaque
service réagit aux événements des autres et publie les siens. Le processus n'est écrit nulle part ; il émerge des
abonnements. Les démos 7 et 8 utilisaient la chorégraphie pour notifier ; ici, elle conduit tout un processus métier.

**À observer** (détails : [Docs/21-choregraphie.md](Docs/21-choregraphie.md), requêtes dans `Choreo.Api\Banque.http`)
- **L'historique causal** : `GET /virements/{id}` montre chaque événement, qui l'a publié, en réaction à quoi. C'est le
  seul fil du processus ; « où en est le virement » demande un service de plus (le Suivi).
- **Compensations dispersées** : un refus de la conformité ou un échec du change sont compensés par Comptes, qui doit
  écouter ces deux événements.
- **Une panne, c'est du silence** : `POST /chaos {"changeEnPanne": true}` → le virement reste en plan, l'événement finit
  aux lettres mortes (`/bus/lettres-mortes`), le virement apparaît dans `/virements/bloques` ; 30 s plus tard, Comptes
  libère de lui-même la réservation.
- **Livré deux fois, traité une fois** : chaque service a sa boîte de réception (testé avec un courtier qui double tout).
- **Ajouter un participant** : le service Fidélité s'abonne à `VirementRegle`, sans qu'aucun autre service ne change.
- `GET /bus` : qui écoute quoi, la seule carte du système, et elle n'existe qu'à l'exécution.

## 22. Strangler Fig (migration progressive) — `22-StranglerFig`

```
Fig.Ancien   :5240  le système à REMPLACER, écrit à l'ancienne (centimes, dates sans fuseau, libellés tronqués) ; deux routes
                    internes ajoutées pour la migration : son journal des modifications et l'import des opérations du nouveau
Fig.Nouveau  :5241  le REMPLAÇANT : son propre modèle, sa propre base, une couche anti-corruption, une synchronisation dans
                    les deux sens, et le contrat historique de l'ancien reproduit à l'identique
Fig.Facade   :5122  le seul point d'entrée des clients : table de routage (Ancien / Ombre / Nouveau) modifiable à chaud,
                    comparaisons du mode ombre, suivi de la migration (/facade, en-tête X-Cle-Admin: demo)
```

```powershell
dotnet run --project 22-StranglerFig\Fig.Ancien     # puis, dans deux autres terminaux :
dotnet run --project 22-StranglerFig\Fig.Nouveau
dotnet run --project 22-StranglerFig\Fig.Facade     # http://localhost:5122
```

Toutes les autres démos partent de zéro ; celle-ci part d'un **système existant**, qu'on ne peut ni arrêter ni réécrire
d'un coup. Une façade se place devant lui, puis lui retire ses fonctions une route à la fois au profit d'un nouveau
service, jusqu'au jour où plus rien ne l'appelle.

**À observer** (détails : [Docs/22-strangler-fig.md](Docs/22-strangler-fig.md), requêtes dans `Fig.Facade\Banque.http`)
- **Qui répond ?** L'en-tête `X-Servi-Par` de chaque réponse. Au départ, la consultation est migrée, les opérations en mode
  ombre, le relevé et les virements encore à l'ancien. Tout ce qui n'est pas déclaré (`/agences`) va à l'ancien.
- **Bascule à chaud, dans les deux sens** : `PUT /facade/routes/virements {"mode": "Nouveau"}`, puis `"Ancien"` pour revenir
  en arrière. L'ancien connaît les virements faits entre-temps par le nouveau : il les a reçus par la synchronisation.
- **Mode ombre** : l'ancien répond, le nouveau est appelé en parallèle et comparé (`GET /facade/comparaisons` : identiques,
  divergences avec les deux corps, appels abandonnés). Le client n'attend jamais le nouveau. Refusé pour une écriture.
- **Synchronisation dans les deux sens** : le nouveau lit le journal de l'ancien (y compris les frais du traitement de
  nuit, qui ne passent pas par la façade) et y reporte ses propres écritures par une boîte d'envoi ; l'écho de ses
  écritures ne revient pas en double, un lot renvoyé n'est importé qu'une fois.
- **Le contrat hérité, bizarreries comprises** : `947.1` et non `947.10`, heure de Paris sans fuseau, libellés tronqués à
  30 caractères, relevé identique au caractère près. 20 tests de contrat envoient les mêmes requêtes aux deux systèmes.
- **Limite visible** : juste après un virement servi par l'ancien, la consultation servie par le nouveau montre l'ancien
  solde, jusqu'à la synchronisation suivante (500 ms). D'où le conseil de migrer ensemble lectures et écritures d'une donnée.
- **Fin de la migration** : `GET /facade/progression` ; `ancienRetirable` n'est vrai que si toutes les routes sont
  migrées **et** que plus aucun appel n'atteint l'ancien. Un appel à `/agences`, la fonction oubliée, suffit à l'empêcher.

## 23. REST, gRPC, GraphQL (styles d'API) — `23-RestGrpcGraphQL`

```
Styles.Banque     le MÉTIER, commun aux trois API, qui ignore tout de HTTP (test d'archi) : lectures unitaires ET groupées,
                  virement, abonnement aux nouvelles opérations, compteur de lectures « en base »
Styles.Outillage  ce que fourniraient Google.Protobuf, Grpc.AspNetCore et HotChocolate, écrit à la main (aucun package) :
                  encodeur Protobuf, trames et trailers gRPC, client et hébergement gRPC, mini-moteur GraphQL
Styles.Contrats   banque.proto et ses classes (un test vérifie qu'elles correspondent champ par champ)
Styles.Serveur    :5123  REST (/clients, /comptes, /virements) et GraphQL (POST /graphql), en HTTP/1.1
                  :5250  gRPC (banque.Banque/…), en HTTP/2 seulement
Styles.Client     la même application écrite trois fois ; joue les mêmes scénarios en comptant requêtes et octets
```

```powershell
dotnet run --project 23-RestGrpcGraphQL\Styles.Serveur    # puis, dans un autre terminal :
dotnet run --project 23-RestGrpcGraphQL\Styles.Client     # (-- --latence 0 pour ne pas simuler de réseau mobile)
```

Les autres démos changent l'intérieur du système ; celle-ci garde un intérieur unique et change sa **surface**. Ce qui
varie : qui décide de la forme des réponses, ce qui circule sur le réseau, et ce que HTTP fait encore pour vous.

**À observer** (détails : [Docs/23-rest-grpc-graphql.md](Docs/23-rest-grpc-graphql.md), requêtes dans `Styles.Serveur\Banque.http`)
- **L'écran d'accueil d'Alice** (3 comptes et leurs dernières opérations) : REST **5 requêtes** (le client, ses comptes,
  puis une par compte), GraphQL et gRPC **une seule**. Corps reçus : 2 014, 881 et 378 octets ; mêmes données à l'octet près.
- **Le N+1 se déplace** : la requête GraphQL unique pourrait lire la base une fois par compte ; le moteur exécute par
  niveaux et le champ `operations` a un résolveur groupé : une lecture (`GET /statistiques`).
- **Une erreur, trois langues** : `422` + `problem+json` ; `200` + `grpc-status: 9` dans les **trailers** HTTP/2 ;
  `200` + `errors` à côté des données obtenues, avec le chemin du champ et la propagation des `null` non nuls.
- **Pousser ou interroger** : le flux gRPC `SuivreOperations` reçoit chaque opération dès qu'elle a lieu, jusqu'à
  l'échéance (`grpc-timeout`, appliquée aussi par le serveur) ; en REST, l'**ETag** rend l'interrogation peu coûteuse (`304`).
- **Faire évoluer** : numéros de champs Protobuf et champs inconnus ignorés ; `@deprecated` dans le schéma GraphQL
  (`GET /graphql/schema`) et le compteur de ceux qui lisent encore le champ (`GET /graphql/utilisation`).
- **GraphQL se protège** : requête validée contre le schéma avant exécution, profondeur limitée à 6 niveaux.

---

## Comparatif

| | Couches | Hexagonale | Onion | Clean | Cœur fonctionnel | Vertical Slice | Composants | Monolithe modulaire | Microservices | CQRS | Event Sourcing | Serverless | Microkernel | Saga orchestrée | Multi-tenant | BFF | Cellulaire | Acteurs | Spatiale | Pipeline | Chorégraphie | Strangler Fig | REST / gRPC / GraphQL |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---| --- | --- |
| Découpage | couches techniques empilées | intérieur / extérieur | couches concentriques | cercles + un use case par classe | pur / impur | par fonctionnalité | par sous-domaine | par sous-domaine, modules autonomes | par sous-domaine, processus séparés | écriture / lecture | décider / appliquer / projeter | par fonction déclenchée par un événement | noyau minimal + plugins | un orchestrateur, des participants | par locataire (client), au niveau des données | un backend par application cliente | par groupe de clients : N copies complètes du système | un acteur par entité (compte, virement), état privé | des unités identiques, chacune avec toutes les données en mémoire | par étape d'un traitement : un filtre par étape | par service ; le processus émerge de leurs réactions | par route : l'ancien et le nouveau côte à côte derrière une façade, le temps de migrer | par style d'API : trois façades fines sur le même métier |
| Le métier dépend… | de la couche données | de rien | de rien | de rien | de rien | du DbContext | de rien (hors son composant) | des contrats des autres modules | du JSON des autres services | de rien (côté écriture) | de rien | des services gérés du fournisseur | de rien (les plugins dépendent du SDK) | des participants, via l'orchestrateur | de rien (le locataire est ambiant) | des services en aval (par HTTP) | de rien (chaque cellule est autonome) | de rien (un acteur ne connaît que des adresses) | de rien (seulement la réplique de son unité) | de rien (un filtre ne connaît que son entrée et sa sortie) | des contrats d'événements, jamais des autres services | de rien (l'ancien n'est vu qu'à travers la couche anti-corruption) | de rien (il ignore HTTP, JSON, Protobuf et GraphQL) |
| Ce qu'on stocke | l'état | l'état | l'état | l'état | l'état | l'état | l'état | l'état | l'état | l'état + des vues | **les événements** | l'état, dans des services gérés | l'état | l'état + l'état de chaque saga | l'état, étiqueté par locataire (base partagée ou dédiée) | rien, sauf les sessions (le métier est dans les services) | l'état, réparti entre les cellules (+ la table de placement) | l'état, une ligne par acteur | l'état, en mémoire dans chaque unité ; la base mise à jour en différé | l'état ; les données ne font que passer | l'état de chaque service + le journal des événements | l'état, **deux fois** (ancien et nouveau), synchronisé dans les deux sens | l'état |
| Projets | 4 | 6 | 5 | 5 | 3 | 2 | 4 | 8 | 7 | 5 | 5 | 4 (dont la plateforme simulée) | 8 (dont 4 plugins) | 4 | 4 | 4 (dont les services simulés) | 4 (dont le lanceur) | 4 (dont le runtime) | 4 (dont l'intergiciel simulé) | 4 (dont le moteur) | 4 (dont le courtier simulé) | 4 (dont l'ancien système) | 6 (dont l'outillage écrit à la main et le client) |
| Déploiements | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | **5** | 1 | 1 | **1 par fonction** (12) | 1 (+ plugins déposés à chaud) | 1 orchestrateur + N participants | **1 pour tous les clients** | 1 par application cliente (+ les services) | 1 routeur + **N cellules**, mises à jour par vagues | 1 (ou une grappe de serveurs avec Orleans, Akka) | **N unités**, ajoutées et retirées selon la charge | 1 | 1 par service + le courtier | 3 : façade, ancien, nouveau ; puis 2 une fois l'ancien éteint | 1 (deux ports : HTTP/1.1 pour REST et GraphQL, HTTP/2 pour gRPC) |
| Abstractions | DAO défini par les données | ports | repository + UoW | boundaries + presenters + gateway | aucune dans le cœur | aucune | 1 interface par composant | contrats + événements + `IModule` | API HTTP + messages | commandes, requêtes, événements, projections | magasin d'événements, flux, projections | déclencheurs + contexte + manifeste | points d'extension (SDK) | étapes : exécuter / compenser | contexte de locataire, résolveurs, fabrique de bases | contrats recopiés, vues par écran | clé de partition, table de placement, plan de contrôle | acteur, adresse, message, superviseur | grille de données, grille de messages, pompe, unité | filtre, tuyau, étage, bilan | événement, abonnement, corrélation, cause, boîte de réception | table de routage, mode (ancien / ombre / nouveau), journal des modifications, boîte d'envoi | ressources et représentations ; contrat .proto et messages ; schéma et résolveurs (groupés) |
| Communication | appels directs | appels de ports | appels directs | boundaries | appels directs | appels directs | interface | interface **et** événements | HTTP **et** courtier de messages | événements (écriture → lecture) | flux d'événements | événements : HTTP, files, stockage, minuterie | appels du noyau vers les plugins | commandes de l'orchestrateur + événements extérieurs | appels directs | HTTP vers les services, en parallèle | routeur → cellule ; entre cellules, rare et idempotente | messages : envoyer (sans réponse) ou demander (avec délai) | réplication asynchrone entre unités ; pompe vers la base | tuyaux bornés entre filtres, en flux | événements uniquement, publiés sans connaître les abonnés | HTTP : façade → l'un ou l'autre ; synchronisation par journal (ancien → nouveau) et import (nouveau → ancien) | REST : verbes HTTP et JSON ; gRPC : HTTP/2, Protobuf, flux ; GraphQL : POST /graphql et JSON |
| Tests typiques | service + faux DAO (entités de persistance) | unitaires + adapters de test | unitaires avec faux | interacteur + espion de sortie | fonctions pures, sans mock | intégration HTTP | boîte noire par composant | parcours inter-modules + module isolé | chaque service seul, faux serveurs HTTP | événements produits / vues obtenues à partir d'événements | Given / When / Then sur des événements | fonctions appelées comme des méthodes + système complet | noyau + plugins en mémoire, vraies DLL, déchargement | horloge manuelle : réessais, échéances, reprise | isolation : un locataire ne voit ni ne modifie l'autre ; quotas | BFF + vrais services en mémoire : agrégation, sécurité, dégradation | système complet en mémoire : rayon d'impact, vagues, migration | acteur-jouet instrumenté : 1 000 messages simultanés, pannes, passivation | réplication retenue à la main : collisions certaines ; élasticité, démarrage à froid | filtres un par un ; pipelines de test assemblés à la main ; ordre, contre-pression, reprise | chaîne des causes ; courtier qui double tout ; horloge manuelle pour l'expiration ; test d'archi | tests de contrat (mêmes requêtes aux deux systèmes) ; liaisons coupées ou qui perdent les réponses ; mode ombre | les trois clients contre le même serveur : même résultat, requêtes et octets comptés ; format filaire octet par octet ; .proto comparé aux classes |
| Garde-fou | références non transitives | discipline (+ test d'archi) | références de projets | références de projets | séparation des projets | discipline | **compilateur** (`internal`) | `internal` + projets Contrats + test d'archi | **le réseau** : impossible de tricher | références de projets (+ test d'archi) | magasin en ajout seul + versions | **la plateforme** : pas d'état possible entre deux appels | un contexte de chargement par plugin | le moteur : état enregistré à chaque pas | filtre global + estampillage + clé composite (+ test d'archi) | aucune référence entre BFF (test d'archi) ; droits vérifiés par les services | **la cellule** : une panne ou un bogue ne la quitte pas | **la boîte aux lettres** : un message à la fois ; le superviseur remplace l'acteur fautif | versions (la plus grande gagne) + routage par affinité | types vérifiés à l'assemblage ; tuyaux bornés (contre-pression) | aucune référence entre services (test d'archi) ; délais d'expiration locaux | la façade : ce qui n'est pas déclaré reste à l'ancien ; retour arrière en une requête ; mode ombre refusé aux écritures | le .proto (test de correspondance) ; le schéma GraphQL, validé avant exécution ; test d'archi (métier sans HTTP) |
| Coût caché | — | — | — | cérémonie | — | duplication possible | — | — | pannes réseau, idempotence, cohérence à terme, exploitation | données dupliquées, cohérence à terme, projections à maintenir | événements immuables à versionner, effacement RGPD, courbe d'apprentissage | démarrages à froid, idempotence partout, dépendance au fournisseur, débogage distribué | SDK à versionner, plugins dans le processus | participants idempotents, orchestrateur à exploiter | fuite entre clients au moindre oubli, voisin bruyant, migrations sur N bases | un composant de plus par application, duplication entre BFF, tentation d'y mettre du métier | échanges inter-cellules, données réparties, N fois l'infrastructure, migrations de clients | pas de transaction entre acteurs, lectures transversales, cycles de demandes, débogage asynchrone | collisions (écritures perdues), tout en mémoire, base en retard, exploitation de la grille | traitements en flux seulement ; filtres à état uniques ; ordre à préserver ; reprise idempotente | processus écrit nulle part ; compensations dispersées ; pannes silencieuses ; projection de suivi à maintenir | deux systèmes à faire vivre ensemble ; synchronisation (boucles, pannes, retard) ; contrat hérité à reproduire ; migration qui s'éternise | REST : allers-retours, sur-récupération ; gRPC : HTTP/2, binaire, pas de navigateur ; GraphQL : N+1 caché, requêtes à borner, plus de cache HTTP |

La progression 6 → 7 → 8 montre un même découpage par sous-domaine de plus en plus isolé : visibilité `internal`, puis
modules autonomes dans un processus, puis services séparés par le réseau. Chaque étape apporte de l'indépendance
(équipes, déploiements, montée en charge) et coûte de la complexité. On ne passe à la suivante que si ce besoin est réel :
**un monolithe modulaire bien découpé est le point de départ recommandé**, et ses modules deviennent des microservices
le jour où il le faut.

Ces approches se combinent, et la démo 7 en est l'exemple : un **monolithe modulaire** au niveau macro, puis, à l'intérieur
de chaque module, l'architecture qui convient (**tranches verticales** pour un module CRUD, **hexagonale** ou **cœur fonctionnel**
pour un module aux règles riches), avec des **ports & adapters** autour des services externes. **CQRS** se pose
par-dessus n'importe laquelle, localement, dans le module dont les lectures le justifient (ici : relevés, historique, tableau de bord).
L'**Event Sourcing** va encore un cran plus loin, et seulement pour les modules où l'historique est lui-même une donnée métier.

Le **DDD** n'a pas de colonne dans le tableau : ce n'est pas une architecture concurrente, mais la démarche qui dit
**où couper**. Ce sont ses contextes délimités qui donnent les modules (démo 7) ou les services (démo 8), et ses agrégats
qui donnent les flux d'événements (démo 10). Ses objets-valeurs et son langage améliorent n'importe laquelle des autres démos.

Le **multi-tenant** (démo 15) est lui aussi transversal : il ne dit pas comment organiser le code, mais comment **partager**
une application entre plusieurs clients sans les mélanger, quelle que soit son organisation interne. Il se combine avec le
**Microkernel** quand les clients veulent des comportements différents (et pas seulement des paramètres), et avec les
**microservices**, où chaque service doit alors propager le locataire et l'appliquer à ses propres données.

Le **BFF** (démo 16) se place devant n'importe lequel de ces systèmes, côté clients : quand plusieurs applications (site,
mobile, partenaires) consomment les mêmes services avec des besoins différents, chacune reçoit un backend à sa mesure.
Il prolonge naturellement les **microservices** (démo 8), dont il agrège les appels, et cohabite avec leur passerelle.

L'**architecture cellulaire** (démo 17) est le dernier cran de l'isolation : après les modules, les services et les
fonctions, on duplique le système **entier** pour que chaque copie ne serve qu'une partie des clients. Elle se pose
au-dessus de n'importe quelle architecture interne (une cellule peut être un monolithe modulaire ou des microservices)
et ne se justifie qu'à grande échelle, quand une panne générale coûte plus cher que N fois l'infrastructure.

Le **modèle d'acteurs** (démo 18) répond à une autre question : non pas comment découper ou déployer, mais **comment
gérer la concurrence**. Au lieu de protéger un état partagé (transactions, verrous), il n'en partage aucun : chaque
entité est un acteur, propriétaire de son état. Il se loge à l'intérieur d'un service ou d'un module, se combine avec
l'**Event Sourcing** (un acteur peut stocker ses événements plutôt que son état, comme Akka Persistence) et avec le
**CQRS** pour les lectures transversales qu'il rend difficiles ; un processus entre acteurs s'écrit comme une petite
**saga** (démo 14).

L'**architecture spatiale** (démo 19) s'attaque au dernier point commun de toutes les autres : la base de données sur le
chemin de chaque requête. En la remplaçant par une grille en mémoire, elle gagne l'élasticité et la tenue aux pics, et
paie en cohérence (collisions possibles, base en retard). Elle partage avec les **acteurs** l'idée de donner chaque
donnée à un seul propriétaire (le routage par affinité), et avec le **CQRS** une base de lecture mise à jour en différé.

Le **pipeline** (démo 20) n'organise pas une application mais un **traitement** : import, export, ETL, flux. Il se loge
dans un service ou un module organisé selon n'importe quelle autre démo, et reprend à son échelle des idées vues
ailleurs : des étapes indépendantes comme les **tranches verticales**, la **contre-pression** des files de messages,
l'**idempotence** qui permet de rejouer (démos 8 et 14).

La **chorégraphie** (démo 21) referme la boucle ouverte par la démo 14 : le même virement, sans chef d'orchestre. La
comparaison est sans appel pour un processus de cette taille : la chorégraphie découple les services et rend l'ajout
d'une réaction trivial, mais disperse la compensation et les délais, et transforme une panne en silence. D'où l'usage
courant : **orchestrer les processus, chorégraphier les réactions**.

Le **Strangler Fig** (démo 22) ne choisit pas une architecture, il dit **comment y arriver** depuis un système existant :
la cible peut être n'importe laquelle des démos précédentes. Il en réutilise beaucoup d'idées : une façade comme la
**passerelle** des microservices (démo 8), une **couche anti-corruption** comme le DDD (démo 11), une **boîte d'envoi**,
l'**idempotence** et la **cohérence à terme** comme partout où deux bases doivent s'accorder. C'est souvent ainsi qu'un
monolithe devient modulaire ou se découpe en services : une route, puis une autre, sans jour J.

La démo **REST, gRPC, GraphQL** (23) regarde enfin la **surface** du système, que toutes les autres exposaient en REST
sans le dire. Le style d'API ne change rien au métier (un test le vérifie) mais tout à ce que paie le client :
allers-retours, octets, erreurs, flux. Il se choisit **par frontière** et se combine : **gRPC entre les services** des
démos 8, 14 ou 17, où le débit et les contrats stricts comptent ; **REST** pour une API publique, où le cache HTTP et la
simplicité comptent ; **GraphQL** devant des écrans riches et variés, là où l'on écrirait sinon un **BFF** par
application (démo 16), avec lequel il partage l'idée d'une réponse taillée pour l'écran.

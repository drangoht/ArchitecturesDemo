# Démos d'architectures en C#

Le **même métier bancaire** (consulter un compte, retirer de l'argent, faire un virement) implémenté avec douze architectures, plus une démo de conception pilotée par le domaine (DDD).

📘 **Documentation complète, avec schémas : [`Docs/`](Docs/README.md)**.
On peut ainsi comparer la **forme** du code à fonctionnalités égales. La démo 0 (couches classiques) sert de point de comparaison :
toutes les autres sont nées pour corriger l'une de ses limites.

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

Données de démo, identiques partout :
- **Alice** `11111111-1111-1111-1111-111111111111` : 1 000 € (5 000 € dans la démo 4, pour tester le plafond)
- **Bob** `22222222-2222-2222-2222-222222222222` : 500 €

Tout tourne en mémoire (EF Core InMemory ou dictionnaires) : aucune base à installer.

## Lancer

```powershell
# Tout compiler et tester (153 tests)
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

## Comparatif

| | Couches | Hexagonale | Onion | Clean | Cœur fonctionnel | Vertical Slice | Composants | Monolithe modulaire | Microservices | CQRS | Event Sourcing | Serverless |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Découpage | couches techniques empilées | intérieur / extérieur | couches concentriques | cercles + un use case par classe | pur / impur | par fonctionnalité | par sous-domaine | par sous-domaine, modules autonomes | par sous-domaine, processus séparés | écriture / lecture | décider / appliquer / projeter | par fonction déclenchée par un événement |
| Le métier dépend… | de la couche données | de rien | de rien | de rien | de rien | du DbContext | de rien (hors son composant) | des contrats des autres modules | du JSON des autres services | de rien (côté écriture) | de rien | des services gérés du fournisseur |
| Ce qu'on stocke | l'état | l'état | l'état | l'état | l'état | l'état | l'état | l'état | l'état | l'état + des vues | **les événements** | l'état, dans des services gérés |
| Projets | 4 | 6 | 5 | 5 | 3 | 2 | 4 | 8 | 7 | 5 | 5 | 4 (dont la plateforme simulée) |
| Déploiements | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | **5** | 1 | 1 | **1 par fonction** (12) |
| Abstractions | DAO défini par les données | ports | repository + UoW | boundaries + presenters + gateway | aucune dans le cœur | aucune | 1 interface par composant | contrats + événements + `IModule` | API HTTP + messages | commandes, requêtes, événements, projections | magasin d'événements, flux, projections | déclencheurs + contexte + manifeste |
| Communication | appels directs | appels de ports | appels directs | boundaries | appels directs | appels directs | interface | interface **et** événements | HTTP **et** courtier de messages | événements (écriture → lecture) | flux d'événements | événements : HTTP, files, stockage, minuterie |
| Tests typiques | service + faux DAO (entités de persistance) | unitaires + adapters de test | unitaires avec faux | interacteur + espion de sortie | fonctions pures, sans mock | intégration HTTP | boîte noire par composant | parcours inter-modules + module isolé | chaque service seul, faux serveurs HTTP | événements produits / vues obtenues à partir d'événements | Given / When / Then sur des événements | fonctions appelées comme des méthodes + système complet |
| Garde-fou | références non transitives | discipline (+ test d'archi) | références de projets | références de projets | séparation des projets | discipline | **compilateur** (`internal`) | `internal` + projets Contrats + test d'archi | **le réseau** : impossible de tricher | références de projets (+ test d'archi) | magasin en ajout seul + versions | **la plateforme** : pas d'état possible entre deux appels |
| Coût caché | — | — | — | cérémonie | — | duplication possible | — | — | pannes réseau, idempotence, cohérence à terme, exploitation | données dupliquées, cohérence à terme, projections à maintenir | événements immuables à versionner, effacement RGPD, courbe d'apprentissage | démarrages à froid, idempotence partout, dépendance au fournisseur, débogage distribué |

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

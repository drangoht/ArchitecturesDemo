# Architecture demos in C#

🌐 [Français](README.md) · English version (translation in progress: [status](Docs/en/TRANSLATION.md))

The **same banking business** (look up an account, withdraw money, make a transfer) implemented with twenty-one
architectures, plus a Domain-Driven Design (DDD) demo, a demo of the progressive migration of a legacy system
(Strangler Fig), a demo that exposes the same business in three ways (REST, gRPC, GraphQL) and a demo that builds the
same screen in MVC and in MVVM. This makes it possible to compare the **shape** of the code with equal features.
Demo 0 (classic layers) is the baseline: every other one was born to fix one of its limits.

> The code (folders, projects, classes, routes, comments) is written in French. Identifiers are quoted as they are;
> a few common words help reading them: *compte* = account, *virement* = transfer, *retrait / retirer* = withdrawal /
> withdraw, *solde* = balance, *métier* = business, *banque* = bank.

📘 **Full documentation, with diagrams: [`Docs/en/`](Docs/en/README.md)** (French original: 🌐 [Français](Docs/README.md)).

📗 **Not a developer? The same thing explained with simple words and pictures: [`Docs/pour-tous/`](Docs/pour-tous/README.md)** (French).

🗂️ **In a hurry? A one-page cheat sheet per architecture: [`Docs/en/fiches/`](Docs/en/fiches/README.md)**, also as a [printable PDF](Docs/en/fiches/pdf/architecture-cheat-sheets.pdf).

⚖️ **Hexagonal or Onion? Saga or choreography? The differences, two by two: [`Docs/differences/`](Docs/differences/README.md)** (French).

| # | Folder | Architecture | French name | Key idea | Port |
|---|---|---|---|---|---|
| 0 | `00-Couches` | Layered / N-tier (classic) | Architecture en couches | Presentation → Business → Data: dependencies flow down to the database | 5100 |
| 1 | `01-Hexagonale` | Ports & Adapters (Cockburn) | Architecture hexagonale | The business at the center, plugged into the world through ports (interfaces) and adapters | 5101 |
| 2 | `02-Onion` | Onion (Palermo) | — | Concentric layers; the center separates the model from domain services | 5102 |
| 3 | `03-Clean` | Clean Architecture (Uncle Bob) | — | One interactor per use case; the result goes through a presenter | 5103 |
| 4 | `04-CoeurFonctionnel` | Functional Core / Imperative Shell | — | 100% pure rules that return a decision; the shell does the I/O | 5104 |
| 5 | `05-VerticalSlice` | Vertical Slice (Bogard) | — | One file per feature, from the route to the database | 5105 |
| 6 | `06-Composants` | Package by Component (Simon Brown) | — | One public interface per component, everything else `internal` | 5106 |
| 7 | `07-MonolitheModulaire` | Modular Monolith | Monolithe modulaire | Autonomous modules (API, database, events), connected by contracts, in a single deployment | 5107 |
| 8 | `08-Microservices` | Microservices | — | One process per service, communication over HTTP and messages, no shared code | 5200 (gateway) |
| 9 | `09-CQRS` | CQRS | — | One model to write (rules), another to read (views ready to serve), connected by events | 5108 |
| 10 | `10-EventSourcing` | Event Sourcing | — | Store not the state but the sequence of events; the state is recomputed by replaying them | 5109 |
| 11 | `11-DDD` | Domain-Driven Design | — | Model the business in its language: value objects, aggregates, bounded contexts, anti-corruption | 5110 |
| 12 | `12-Serverless` | Serverless (FaaS) | — | Stateless functions triggered by events; the provider manages the servers and bills per use | 5111 |
| 13 | `13-Microkernel` | Microkernel (plug-ins) | — | A minimal core; checks, fees and statement formats are plug-ins loaded at runtime | 5112 |
| 14 | `14-SagaOrchestree` | Orchestrated Saga | Saga orchestrée | An orchestrator drives a 6-step transfer: persistent state, retries, waiting, compensation | 5113 |
| 15 | `15-MultiTenant` | Multi-tenant (SaaS) | — | A single application for several customer banks: isolated data, rules and quotas per customer | 5114 |
| 16 | `16-BFF` | Backend for Frontend | — | One backend per client application (site, mobile): aggregation, screen-tailored responses, token kept on the server | 5115 (web), 5116 (mobile) |
| 17 | `17-CellBased` | Cell-Based Architecture | Architecture cellulaire | Several complete, independent copies of the system, each for part of the customers: bounded blast radius | 5117 (router) |
| 18 | `18-ModeleActeurs` | Actor Model | Modèle d'acteurs | One actor per account and per transfer: private state, messages handled one at a time, no locks; a crashed actor is replaced | 5118 |
| 19 | `19-SpaceBased` | Space-Based Architecture | Architecture spatiale | All the data in memory, replicated between processing units added with the load; the database off the request path | 5119 |
| 20 | `20-Pipeline` | Pipeline (pipes & filters) | — | Independent filters connected by bounded pipes, working at the same time on a file of transfers | 5120 |
| 21 | `21-Choregraphie` | Event-Driven Choreography | Chorégraphie | Demo 14's transfer without an orchestrator: each service reacts to the others' events | 5121 |
| 22 | `22-StranglerFig` | Strangler Fig (progressive migration) | — | A façade in front of the legacy system; its features move to the new one a route at a time, reversibly, until it can be switched off | 5122 (façade) |
| 23 | `23-RestGrpcGraphQL` | REST, gRPC, GraphQL (API styles) | — | The same business exposed as resources, as procedures and as a graph queried by the client: requests, bytes, errors, streaming and evolution compared | 5123 (REST, GraphQL), 5250 (gRPC) |
| 24 | `24-MvcMvvm` | MVC and MVVM (presentation patterns) | — | The same screen twice: web MVC (the controller receives the request and picks a view, discarded after rendering) and MVVM (a living ViewModel, testable without a screen, observed by WPF and console views) | 5124 (MVC site) |

Demo data, identical everywhere:
- **Alice** `11111111-1111-1111-1111-111111111111`: €1,000 (€5,000 in demo 4, to test the limit)
- **Bob** `22222222-2222-2222-2222-222222222222`: €500

In demo 15, Alice and Bob exist at each of the three customer banks, with different balances.
In demo 16, they are users (`alice` / `bob`, password `demo`); Alice also has a savings account (Livret A)
`44444444-4444-4444-4444-444444444444` (€8,000).
In demo 22, the legacy system identifies accounts by a number: Alice `10001`, Bob `10002` (same balances).
In demo 23, the customers are `alice` and `bob`; Alice has three accounts (current `10001`, Livret A `10002`, joint
account `10003`) with their September transactions, Bob a current account `20001`.
In demo 24, Alice also has a Livret A `44444444-4444-4444-4444-444444444444` (€8,000), as in demo 16.

Everything runs in memory (EF Core InMemory or dictionaries): no database to install.

## Running

```powershell
# Tout compiler et tester (489 tests)
dotnet test ArchitecturesDemo.slnx

# Lancer une API (chaque dossier a aussi sa propre solution .slnx)
dotnet run --project 01-Hexagonale\Hexa.Adapters.Web
```

Each API contains a **`Banque.http`** file with requests ready to send (Visual Studio, Rider or VS Code's REST Client extension).

---

## 0. Layered (N-tier) — `00-Couches`

```
Couches.Presentation   MVC controllers                                  (→ Metier only)
Couches.Metier         ComptesService: all the rules, CompteDto         (→ Donnees)
Couches.Donnees        CompteEntite (anemic), BanqueDbContext, IComptesDao + ComptesDao
```

**What to look at**
- **The direction of dependencies**: the business layer depends on the data layer. The data layer defines `IComptesDao`
  and `CompteEntite`, in its own vocabulary, and the business layer adapts to it. Compare with `02-Onion`, where the arrows
  are reversed: the domain defines `IComptesRepository` and the infrastructure implements it. This is **the** difference
  between layers and domain-centric architectures.
- **Anemic entity**: `CompteEntite` only has public properties. The rules live in `ComptesService` (*transaction script*
  style). Nothing stops other code from doing `compte.Solde -= x` without any check (see the comment in the service).
- **Tests coupled to persistence**: even the fake DAO in `ComptesServiceTests` handles `CompteEntite` objects. A schema
  change breaks the business tests.
- **Strict layers**: the presentation only sees the layer right below it. `DisableTransitiveProjectReferences` in
  `Couches.Presentation.csproj` prevents using `BanqueDbContext` from a controller (error CS0234 at compile time),
  and it is `AddCoucheMetier()` that plugs in the data layer itself.
- It is the simplest approach to understand, and it remains perfectly valid for a CRUD application with thin business logic.

---

## 1. Hexagonal — `01-Hexagonale`

```
Hexa.Hexagone                 ← NO dependencies
  Domaine/                       Compte, business exceptions
  Ports/Entrants/                IConsulterCompte, IRetirerArgent, IEffectuerVirement   (inbound ports)
  Ports/Sortants/                IComptes, INotifications                               (outbound ports)
  Application/                   implementations of the inbound ports
Hexa.Adapters.Persistance     ← 2 adapters of the SAME port: ComptesEnMemoire, ComptesFichierJson
Hexa.Adapters.Notifications   ← NotificationsJournal
Hexa.Adapters.Web             ← inbound HTTP adapter + composition root
Hexa.Adapters.Cli             ← 2nd inbound adapter: the command line
```

**What to look at**
- An adapter is swapped by **configuration**, without touching the business code:
  `dotnet run --project Hexa.Adapters.Web -- --Persistance=Json`
- Two entry points into the same hexagon. Started from `01-Hexagonale\`, the API (in Json mode) and the CLI share `comptes.json`:
  ```powershell
  dotnet run --project Hexa.Adapters.Cli -- solde 11111111-1111-1111-1111-111111111111
  dotnet run --project Hexa.Adapters.Cli -- virer 11111111-1111-1111-1111-111111111111 22222222-2222-2222-2222-222222222222 50
  ```
- `Hexa.Tests\Adapters\ContratIComptesTests.cs`: **contract tests**. The same tests run against every adapter of the
  port, which guarantees they are interchangeable.
- The CLI wires everything by hand (`new RetirerArgent(comptes, notifications)`): the hexagon doesn't need a
  dependency injection container.

## 2. Onion — `02-Onion`

```
Onion.Domain          Model/ (Compte)  →  Services/ (ServiceVirement)  →  Interfaces/ (IComptesRepository, IUnitOfWork)
Onion.Application     ComptesAppService, VirementsAppService, DTOs          (→ Domain)
Onion.Infrastructure  BanqueDbContext (EF Core), ComptesRepository          (→ Application)
Onion.Api             composition root, error middleware                    (→ everything)
```

**What to look at**
- `ServiceVirement` is a **domain service**: the rule (€5,000 limit, no transfer to oneself) concerns two accounts and
  belongs to neither.
- `VirementsAppService` **orchestrates** without deciding: it loads, delegates to the domain, then saves once through
  `IUnitOfWork` (all or nothing).
- The EF Core mapping is in `BanqueDbContext.OnModelCreating`: the domain carries no EF attributes.
- Business errors are translated into HTTP **once**, by a middleware.

## 3. Clean Architecture — `03-Clean`

```
Clean.Entites               Compte                                                         (circle 1)
Clean.CasUtilisation        per use case: Demande/Reponse, IxxxEntree, IxxxSortie, Interacteur  (circle 2)
Clean.AdaptateursInterface  Controleurs/, Presenters/, Passerelles/                         (circle 3)
Clean.Web                   ASP.NET Core: just plumbing                                     (circle 4)
```

**What to look at**
- The interactor **returns nothing**: it calls `sortie.Succes(...)`, `sortie.Refus(...)` or `sortie.CompteIntrouvable(...)`
  (success, refusal, account not found).
- **Two presenters for the same interactor**: `GET /comptes/{id}` returns JSON, `GET /comptes/{id}?format=texte` a text
  statement. The interactor was not changed for that.
- The adapters don't depend on ASP.NET Core (checked by `ArchitectureTests`): the presenters produce a `VueHttp`, which
  `Clean.Web` writes into the response.
- It is the most "ceremonious" variant: compare the number of types with demo 5 for the same feature.

## 4. Functional Core / Imperative Shell — `04-CoeurFonctionnel`

```
Fonctionnel.Coeur     Compte (immutable record), DecisionRetrait/DecisionVirement, Regles (pure functions)
Fonctionnel.Coquille  API + EF Core + clock + log: READ → DECIDE → APPLY
```

**What to look at**
- `Regles.Virer(source, cible, montant, dejaVireAujourdhui)`: the day's running total (which depends on the database
  and the time) is **computed by the shell and passed as a parameter**. The core never calls `DateTime.Now`.
- The tests (`ReglesTests`) have **no mocks**: values in, a decision out.
- In `Endpoints.cs`, each endpoint follows the same commented three-step pattern.
- To test the €2,000 daily limit: a €1,500 transfer goes through, then a €1,000 one is refused.

## 5. Vertical Slice — `05-VerticalSlice`

```
Vertical.Api
  Features/Comptes/OuvrirCompte.cs       ← transaction script: everything is in the file
  Features/Comptes/ConsulterCompte.cs    ← read: direct LINQ projection, AsNoTracking
  Features/Comptes/RetirerArgent.cs      ← write: validation + rule + persistence
  Features/Virements/EffectuerVirement.cs
  Domain/Compte.cs                       ← only the SHARED rule (sufficient balance)
  Data/BanqueDbContext.cs                ← no repository on top
```

**What to look at**
- To understand "withdraw money", you only need to open **one file**.
- Each slice picks its technique: the read projects straight into the response, the write goes through the entity.
- `Compte.Retirer` was only extracted into `Domain/` because two slices need it (see the class comment).
- The tests are **integration tests per slice** (`WebApplicationFactory`), organized like the code.

## 6. Package by Component — `06-Composants`

```
Composants.Comptes     public: IComptesComposant, CompteDto, Resultat, AddComposantComptes()
                       internal: Compte, ComptesDbContext, ComptesComposant
Composants.Virements   public: IVirementsComposant, VirementDto, AddComposantVirements()
                       internal: Virement, VirementsDbContext, VirementsComposant   (→ IComptesComposant)
Composants.Api         only sees the public interfaces
```

**What to look at**
- In `Composants.Api\Program.cs`, try writing `new Compte(...)` or injecting `ComptesDbContext`: **it doesn't compile**.
  The architecture is enforced by the compiler.
- Each component has **its own database**. Transfers (`Virements`) never reads the Accounts (`Comptes`) tables: it goes
  through `IComptesComposant`.
- With no shared transaction between the two databases, `VirementsComposant` **compensates** if the deposit fails. The
  test `Si_le_depot_echoue_le_retrait_est_compense` checks it with a fake Accounts component.
- `ArchitectureTests` locks the public surface: making an internal type public fails the test.
- `InternalsVisibleTo` (in the `.csproj`) opens the internals to the test project only.

## 7. Modular Monolith — `07-MonolitheModulaire`

```
Modulaire.Partage               IModule, in-memory event bus                 (no business logic)
Modulaire.Comptes.Contrats      IComptesApi, CompteInfo, RetraitEffectue event
Modulaire.Comptes               ComptesModule (only public type) + database, HTTP API, domain   (→ its Contrats)
Modulaire.Virements.Contrats    VirementEffectue event
Modulaire.Virements             VirementsModule + database, HTTP API          (→ Comptes.Contrats, Virements.Contrats)
Modulaire.Notifications         NotificationsModule: only listens to events  (→ Contrats only)
Modulaire.Hote                  assembles the modules: a single process, a single deployment
```

This is the step after components (demo 6). A module is no longer just "business logic behind an interface": it is a
**mini-application** that owns its HTTP routes, its database, its subscriptions, and is plugged in or out with one line.

**What to look at**
- `Modulaire.Hote\Program.cs`: the host only knows `IModule`. Removing `new NotificationsModule()` from the list removes
  the whole feature (routes, database, subscriptions) without breaking the others.
- **`Contrats` projects**: Transfers needs the accounts, but only references `Modulaire.Comptes.Contrats`, never
  `Modulaire.Comptes`. The implementation of `IComptesApi` is provided by the host, through dependency injection.
- **Two communication modes**:
  - *synchronous*: Transfers calls `IComptesApi.Debiter/Crediter` (debit/credit), because it needs the answer right away;
  - *asynchronous*: Accounts publishes `RetraitEffectue` and Transfers publishes `VirementEffectue`, without knowing who listens.
- **Notifications** was added without touching Accounts or Transfers: it subscribes to the events and keeps its own
  database. The event carries the account holders' names, so that the subscriber doesn't have to call Accounts back.
- **One database per module** (`Banque-Comptes`, `Banque-Virements`, `Banque-Notifications`). With no shared transaction,
  Transfers **compensates** the debit if the credit fails.
- **Isolation** (`IsolationTests`): a module is tested by replacing another one with a fake through its contract. A
  failure in a subscriber doesn't make the publishing module fail.
- `ArchitectureTests` checks the rules: a module only references `Contrats` and `Partage`, only exposes its `IModule`
  class, and contracts depend on no module.
- The bus is deliberately simple: synchronous and in-process. In production, you would use an *outbox* (event saved in
  the same transaction as the data) and background processing.
- Each module can have its own internal architecture (vertical slices, hexagonal…). The day a module must be deployed
  separately, its contracts become a network API and its bus a real broker: that is the road to microservices.

## 8. Microservices — `08-Microservices`

```
Micro.Passerelle     :5200  single entry point for clients (reverse proxy); doesn't expose the internal APIs
Micro.Comptes        :5201  its database; public API + idempotent internal API (/interne/…); outbox
Micro.Virements      :5202  its database; calls Accounts over HTTP (retry, timeout, circuit breaker); saga + compensation; outbox
Micro.Notifications  :5203  its database; receives messages, ignores duplicates
Micro.Courtier       :5209  mini message broker: "at least once" delivery, retries, dead letters
Micro.Lanceur               demo tool: starts the services the way an orchestrator would
```

```powershell
dotnet build 08-Microservices\Microservices.slnx
dotnet run --project 08-Microservices\Micro.Lanceur                          # tous les services
dotnet run --project 08-Microservices\Micro.Lanceur -- comptes passerelle    # seulement certains
```

This is demo 7 cut into separate processes. Everything that was free in the monolith (method call, transaction,
in-memory event) now goes through the **network**, which can fail. Most of the code is there to handle those failures.

**What to look at**
- **No references between services**, and no shared project. The contract is the JSON: `Micro.Virements\ClientComptes.cs`
  and `Micro.Notifications\Donnees.cs` have their own copies of the messages. The outbox relay is deliberately duplicated.
- **Resilient calls** (`Micro.Virements\Program.cs`): `AddStandardResilienceHandler` adds retry, timeout and circuit breaker.
- **Idempotency**:
  - debits/credits carry an `operationId` (`Micro.Comptes\Endpoints.cs`). A retry after a lost response doesn't debit
    twice (test `Une_panne_passagere_est_absorbee_par_le_retry_sans_double_debit`);
  - Notifications ignores a `Message-Id` it has already handled, since the broker can deliver twice.
- **Outbox**: the message is saved in the same transaction as the data, then a relay sends it to the broker. If the
  broker is down, the withdrawal still succeeds and the event leaves later (test `Si_le_courtier_est_en_panne…`).
- **Saga and compensation** (`Micro.Virements\Endpoints.cs`): the transfer is saved before the calls, with a status
  (`EnCours`, `Effectue`, `Refuse`, `Annule`, `AReprendre`: in progress, done, refused, cancelled, to resume). If the
  credit fails, the source is credited back.
- **Eventual consistency**, to try yourself:
  1. `dotnet run --project Micro.Lanceur -- courtier comptes virements passerelle` (without Notifications);
  2. make a transfer: it succeeds, `/notifications/…` answers 502 and `http://localhost:5209/etat` shows the pending messages;
  3. in a 2nd terminal, `dotnet run --project Micro.Lanceur -- notifications`: a few seconds later, the notifications appear.
- **Gateway**: `GET /sante` aggregates the health of all the services. `/interne/…` answers 404, since only the public
  routes are forwarded.
- **Tests**: each service is tested **alone**, with fake HTTP servers in place of the others (`Micro.Tests\Outils.cs`).
  The full journey is checked by running the system.
- In production, you would replace the broker with RabbitMQ / Azure Service Bus, the gateway with YARP, the launcher with
  docker compose / Kubernetes / .NET Aspire, and add distributed tracing (OpenTelemetry).

## 9. CQRS — `09-CQRS`

```
Cqrs.Evenements   CompteOuvert, ArgentRetire, VirementEffectue: the ONLY link between the two sides
Cqrs.Ecriture     Commandes/ (command + validator + handler), BusCommandes (pipeline), Domaine/Compte (aggregate),
                  EcritureDb: account state + event log, in the same transaction
Cqrs.Lecture      Modele/ (ReleveCompte, LigneOperation, Statistiques: denormalized tables), Projections/Projecteur,
                  Requetes/ (query + handler), BusRequetes; LectureDb: separate database
Cqrs.Api          POST → commands, GET → queries; SynchroniseurProjections (background task)
```

**CQRS** (*Command Query Responsibility Segregation*) starts from an observation: writing and reading don't have the same
needs. Writing wants rules and consistency; reading wants data ready to display, fast, in varied shapes. So the two
models are separated.

**What to look at**
- **Commands** (`Cqrs.Ecriture\Commandes\`): one file per intent, in the imperative (`OuvrirCompte`, `RetirerArgent`…).
  They return **no data**: the API answers `202 Accepted`. The new account's identifier is chosen by the caller.
- **Pipeline** (`BusCommandes`): shape validation (`IValidateur<T>`) runs before any handler and gathers all the errors.
  The business rules (sufficient balance) stay in the aggregate. This is the role often given to MediatR.
- **Aggregate** (`Domaine\Compte.cs`): it has **no** property for display (no history, no statistics). It records what
  happens to it as events, saved with its state in the same transaction.
- **Read model** (`Cqrs.Lecture\Modele\`): one table per screen, with deliberately duplicated data (holder, balance after
  each operation, pre-written labels, totals maintained as you go). The queries only read.
- **Projections** (`Projecteur.cs`): the only code that writes on the read side. The checkpoint makes the projection
  **idempotent** (replaying an event has no effect). The whole read database could be thrown away and rebuilt from the events.
- **Eventual consistency, made visible**: `appsettings.json` slows the projection down by 2 s (`Projections:DelaiMs`).
  Right after a withdrawal, `GET /comptes/{id}` still shows the old balance, and `GET /admin/projections` shows the lag.
  Set 0 for a near-immediate projection.
- **Two independent sides**: neither `Cqrs.Ecriture` nor `Cqrs.Lecture` references the other (checked by
  `ArchitectureTests`). The read-side tests only use events, without a single command.
- **Not to be confused**:
  - CQRS doesn't require two databases. You can separate only the models, over one database, and read synchronously.
    It is the simplest variant, often sufficient. Two databases and eventual consistency are justified when reads are
    very numerous or very different from writes.
  - CQRS is not *Event Sourcing*. Here, the account state is stored normally and the events feed the read side. In
    Event Sourcing, the events **are** the source of truth: the state is rebuilt by replaying them. That is demo 10.

## 10. Event Sourcing — `10-EventSourcing`

```
Es.Domaine       Evenements (CompteOuvert, ArgentDepose, ArgentRetire v2, VirementEmis/Recu, CompteCloture),
                 Compte: aggregate rebuilt by replaying its events
Es.Stockage      MagasinEvenements (append only, optimistic concurrency, .jsonl file), SerialiseurEvenements (+ upcasting),
                 DepotComptes (loading, snapshots, time travel)
Es.Projections   ProjectionReleves, ProjectionPlusGrosRetraits, MoteurProjections (global stream tracking, rebuild)
Es.Api           commands, audit, time travel, projection administration
```

In every other demo, you store the **state** (the balance) and forget how you got there. Here, you store the
**sequence of events** and never delete any of them. The state is just a calculation: the result of replaying the history.

**What to look at**
- **The database is a readable file**: `Es.Api\evenements.jsonl`, one line per event, append only. Stop the API, open
  the file, restart: the whole state (balances, projections) is rebuilt from those lines. To start from scratch,
  delete the file.
- **Decide / apply** (`Es.Domaine\Compte.cs`): the decisions (`Retirer`, `Cloturer`…) check the rules and record an
  event; `Appliquer` evolves the state **without any rule**, because a past fact cannot be refused.
- **Given / When / Then tests** (`CompteTests`): *given* these events, *when* I withdraw €30, *then* I expect
  `ArgentRetire(30)`. Behavior is tested without a database, without mocks, with facts.
- **Optimistic concurrency**: each stream has a version. `If-Match: 1` on a command means "I decide based on version 1".
  If the account has changed since: 409, nothing is written. The transfer writes to two streams, all or nothing.
- **Audit and time travel**: `GET /comptes/{id}/evenements` gives the full history;
  `GET /comptes/{id}/etat?version=2` (or `?date=…`) recomputes the account state as it was at that moment.
- **Disposable projections**: `POST /admin/projections/reconstruire` erases all the views and replays the whole store.
  `ProjectionPlusGrosRetraits` (largest withdrawals) shows the approach's major asset: a view added **after the fact**
  immediately knows the whole history, which is impossible when only the final state was kept.
- **Snapshots** (`DepotComptes`): every 5 events, a picture of the state is cached. Loading an account then only replays
  what follows (test `Un_instantane_evite_de_rejouer…`). It is only a cache: it can be erased without losing anything.
- **Event evolution (upcasting)**: `ArgentRetire` gained a `Motif` (reason) field in v2. The old v1 events are never
  rewritten; `SerialiseurEvenements` converts them on read (reason "Non précisé", i.e. unspecified).
- **The trade-offs**: an event can be neither changed nor deleted. Mistakes are corrected with a new compensating event,
  and GDPR requires planning for erasure (encrypting personal data, then destroying the key). Events must also be
  versioned and projections kept up to date. It is powerful for domains where history has value (banking, accounting,
  logistics, audit), and often overkill for CRUD.
- In production: EventStoreDB, Marten (PostgreSQL) or Axon replace the store hand-written here.

## 11. Domain-Driven Design — `11-DDD`

```
Ddd.TenueDeCompte.Domaine          LANGAGE.md (glossary), ObjetsValeurs/ (Argent, Devise, Iban, NomTitulaire, CompteId),
                                   Comptes/ (Compte aggregate + Mouvement entity, FabriqueDeComptes, IComptes, ServiceDeVirement)
Ddd.TenueDeCompte.Application      ServiceComptes (use cases), PublicateurEvenements (domain → published language)
Ddd.TenueDeCompte.Infrastructure   EF Core (value object mapping), anti-corruption layer toward the legacy exchange system
Ddd.TenueDeCompte.Contrats         PUBLISHED LANGUAGE: VirementEmis, IAbonneTenueDeCompte, ITenueDeCompteApi (open host service)
Ddd.Conformite                     2nd bounded context: LANGAGE.md, DossierDeVigilance, Alerte, SurveillanceDesVirements
Ddd.Api                            hosts both contexts
```

DDD is not a folder layout like the previous demos. It is a **way of designing**: start from the business, speak its
language, and let the model drive the code. It can be applied with a hexagonal, Onion or Clean architecture (here, the
Onion of demo 2). It has two sides. (*Tenue de compte* = Account Keeping, *Conformité* = Compliance.)

**The tactical side: modeling well inside a context**
- **Ubiquitous language**: `LANGAGE.md` lists the business terms; each one is found as is in the code
  (`PlafondRetraitJournalier`, `Titulaire`, `Virement`…: daily withdrawal limit, holder, transfer).
- **Value objects** (`ObjetsValeurs\`): defined by their value, immutable, **always valid**. An `Iban` checks its mod-97
  check digits on creation, so receiving an `Iban` is enough to know it is valid. `Argent` (money) carries its currency
  and refuses to add euros and dollars. `CompteId` avoids mixing up two `Guid`s. Compare with the bare `decimal` and
  `string` of the other demos ("primitive obsession").
- **Aggregate** (`Compte`): a root that guarantees its invariants. Its **boundary** is dictated by a rule: the daily
  withdrawal limit forces the `Mouvement`s (movements) to be part of the aggregate. A movement is never loaded or saved alone.
- **Factory** (`FabriqueDeComptes`): opening an account (number, IBAN calculation, limit) is creation logic that belongs
  neither to the account nor to the application.
- **Domain service** (`ServiceDeVirement`): the transfer touches two aggregates and an exchange rate. It lives in a
  stateless service, still in the domain's language.
- **Repository** (`IComptes`): one per aggregate root, defined in the domain, implemented in the infrastructure.
- **Domain events**: raised by the aggregate, published after saving.

**The strategic side: splitting into contexts and organizing their relationships**
- **Two bounded contexts**, two models for the same reality. For *Account Keeping*, an account has a balance, a limit
  and movements. For *Compliance*, it is a `DossierDeVigilance` (due diligence case) with transfers observed **in euros**
  and alerts, without a balance (see the two `LANGAGE.md`). Wanting a single "Account" model for everyone would produce
  a bloated model.
- **Published language** (`Ddd.TenueDeCompte.Contrats`): the other contexts only see integration events made of simple
  types. `PublicateurEvenements` translates the (rich, internal) domain events into integration events. Compliance only
  references this project (checked by an architecture test).
- **Open host service** (`ITenueDeCompteApi`): what Account Keeping agrees to do for others. On the 3rd transfer within
  10 minutes, Compliance suspects structuring and **requests** that the account be blocked: it doesn't block it itself.
- **Anti-corruption layer** (`CoucheAntiCorruption.cs`): the legacy exchange system returns
  `"EURUSD=10832;EURGBP=8571;…"` (rate × 10,000, from the euro only, with currencies the bank doesn't hold).
  `AdaptateurTauxDeChange` translates this format into `Argent`/`Devise`, computes cross rates (USD → CHF) and drops GBP
  and JPY. Nothing of this format leaks into the domain.
- **Context map** of this demo:
  ```
  [Legacy exchange system] ──(anti-corruption layer)──▶ [Account Keeping] ──(published language + open host service)──▶ [Compliance]
                                                              ▲                                                               │
                                                              └─────────────── block request (ITenueDeCompteApi) ────────────┘
  ```
- When to use it: when the business is **complex** and experts are available to build the language with you. For
  CRUD, it is a lot of ceremony for little gain.

## 12. Serverless — `12-Serverless`

```
Sls.Plateforme   THE SIMULATED CLOUD PROVIDER (never written for real): MoteurExecution (instances, cold start, scaling,
                 recycling, throttling, timeout, billing), managed services (TableGeree with ETag, ServiceFiles with
                 retries and a poison queue, ServiceStockage, settings, log), /plateforme console
Sls.Fonctions    THE CODE YOU WRITE: Deploiement.cs (manifest) + stateless static functions
Sls.Hote         runs the platform locally (the equivalent of "func start" or "sam local")
```

In serverless (*Function as a Service*: AWS Lambda, Azure Functions…), you no longer deploy an application that runs
continuously, but **functions** the provider runs on demand, when an **event** occurs (HTTP request, queued message,
uploaded file, timer). No servers to manage, and you only pay for execution.

The platform is simulated (nothing to download), but it reproduces the behaviors that change the way you code.
`Sls.Fonctions` only contains what you would actually write.

**What to look at** (requests ready in `Sls.Hote\Banque.http`, headers `X-Instance`, `X-Demarrage-A-Froid` (cold start),
`X-Duree-Ms` (duration))
- **No server to write**: `Deploiement.cs` declares the functions and their triggers (HTTP, queue, storage, timer), with
  the allocated memory. Each function is a static method `(trigger, context) → response`.
- **Cold start**: the first call to a function creates an instance (≈ 600 ms here); the next ones reuse the "warm"
  instance (a few ms). After 20 s without a call, the instance is recycled: **scale to zero**, nothing runs anymore.
- **Automatic scaling**: an instance handles only one call at a time. Uploading a file of 8 transfers (`POST /imports`)
  triggers `TraiterImport`, which creates 8 messages (*fan-out*); `ExecuterVirement` then rises to 8 parallel instances
  (`GET /plateforme/fonctions` → `picInstances`). Beyond the maximum: throttling (429).
- **Stateless**: `GET /visites` compares a counter kept in the instance's memory (wrong as soon as there are several
  instances, reset on recycling) with a counter in the managed table (right). Everything that matters goes into the
  managed services.
- **Concurrency**: dozens of instances can modify the same account. Writes are conditional (ETag): 30 simultaneous €100
  withdrawals from a €1,000 account → exactly 10 succeed (test `Des_retraits_simultanes…`).
- **Asynchronous by default**: `POST /virements` answers `202` right away; the transfer is executed by another function,
  triggered by the queue, which then sends notifications into a 2nd queue.
- **Retries and poison messages**: an exception gets the message redelivered (increasing delay). After 3 failures, it
  goes to `notifications-poison`. Scenario: `PUT /plateforme/reglages/Sms:EnPanne` to `true`, make a transfer, observe
  the poison messages, set it back to `false`, then `POST /plateforme/files/notifications-poison/rejouer`.
- **Idempotency is mandatory**: a message can be delivered twice. `Compte.Appliquer` ignores an already known operation,
  notifications have a deterministic key, and a reprocessed import file creates no duplicates.
- **Timeout**: a function that exceeds its timeout is stopped and its instance destroyed (504).
- **Billing**: `GET /plateforme/facture` gives invocations × price + duration × memory (GB-s), at AWS Lambda prices.
  A complete demo costs a few thousandths of a cent; but constant, high traffic can cost more than a server.
- **Tests**: functions are plain methods, tested directly with `ContexteFonction.PourTests` (without the platform);
  the platform and the full system have their own tests.
- Going real: Azure Functions (isolated worker) or AWS Lambda, with Azurite / LocalStack locally, and Durable
  Functions / Step Functions to orchestrate long sequences.

---

## 13. Microkernel — `13-Microkernel`

```
Mk.Contrats      the plug-in SDK: IPlugin, IControleOperation, IRegleFrais, IFormatReleve
Mk.Noyau         NoyauBancaire (accounts, debit, credit), RegistrePlugins, ChargeurPlugins (AssemblyLoadContext)
Mk.Hote          API + watching of the plugins/ folder (automatic reload)
Mk.Plugins.*     PlafondNocturne, ListeNoire, FraisVirement, Exports (CSV + QIF) → built into plugins/{Name}/
```

A minimal core that references **no** plug-in; everything that varies is plugged into extension points (night-time
limit, blacklist, transfer fees, statement exports).

**What to look at** (details: [Docs/en/13-microkernel.md](Docs/en/13-microkernel.md))
- `GET /admin/plugins`: 5 extensions in 4 DLLs, with their state and their extension points.
- **Hot reload**: edit `plugins\Mk.Plugins.ListeNoire\plugin.json`, or rebuild a plug-in **while the API is running**
  (`dotnet build Mk.Plugins.ListeNoire`): it is reloaded automatically. The DLLs are read into memory, so never locked;
  the old load contexts are unloaded.
- `POST /admin/plugins/{nom}/desactiver` / `activer`: change behavior without restarting.
- **The classic trap**, avoided: `Mk.Contrats` is **not** copied into the plug-in folder (`Private="false"`); otherwise
  the plug-in's `IPlugin` would be a different type from the core's. A test checks there is only one copy.
- A crashing plug-in is put "in error" and set aside (the current operation is refused for safety); a plug-in that
  requires a newer core is refused.

## 14. Orchestrated Saga — `14-SagaOrchestree`

```
Saga.Participants   simulated, idempotent services: Comptes (reserve / capture), Conformité, Change, Notifications
Saga.Orchestrateur  DefinitionVirement (the 6 steps and their compensations), MoteurSaga, EtatSaga, MagasinSagas
Saga.Api            start / follow a saga, analyst decision, simulated failures (PUT /chaos), stop / restart
```

Demos 7 and 8 coordinate through **choreography** (each one reacts to the others' events); here, an explicit
**orchestrator** drives the process: ① reserve the funds, ② compliance check, ③ currency exchange, ④ credit,
⑤ final debit (point of no return), ⑥ notification.

**What to look at** (details: [Docs/en/14-saga-orchestree.md](Docs/en/14-saga-orchestree.md); requests in `Saga.Api\Banque.http`)
- `GET /sagas/{id}`: the state of each step and the saga's full log.
- **Transient failure**: `PUT /chaos {"echecsChangeRestants":2}` then a transfer Alice → Carla (USD): 2 retries, then success.
- **Lasting failure**: `{"changeHorsService":true}`: abandoned at the 4th failure, then compensation in reverse order (funds released).
- **Waiting for an external event**: a transfer ≥ €10,000 waits for an analyst's decision
  (`POST /sagas/{id}/conformite`); without a decision before the deadline (60 s), it is compensated.
- **Point of no return**: `{"notificationsHorsService":true}`: after the final debit there is no more compensation, the
  saga goes to `ATraiterManuellement` (to handle manually).
- **Recovery**: `POST /admin/orchestrateur/arreter` during a saga, then `relancer`: it resumes exactly where it was,
  because its whole state is in the store.
- In production: Temporal, Azure Durable Functions, AWS Step Functions, or MassTransit / NServiceBus sagas.

## 15. Multi-tenant — `15-MultiTenant`

```
Mt.Locataires   TENANCY, without business logic: Locataire (plan, isolation, rules, quotas), CatalogueLocataires,
                ContexteLocataire, resolvers (subdomain, header), MiddlewareLocataire, per-tenant rate limiting
Mt.Banque       the business, written as if for a single bank: ServiceBancaire;
                BanqueDbContext (global filter, stamping, cross-tenant writes refused), FabriqueBanque (shared or dedicated database)
Mt.Api          customer API + platform back office (/plateforme: sign-up, plan, suspension, departure, jobs)
```

A **single application, a single deployment**, for several customers (the *tenants* — *locataires* in the code —, here
three banks) who must never see each other's data. It is the SaaS model. The question is no longer "how to organize
the code", but "how to share the application **without mixing** the customers".

**What to look at** (details: [Docs/en/15-multi-tenant.md](Docs/en/15-multi-tenant.md); requests in `Mt.Api\Banque.http`)
- **The business ignores the tenant**: `ServiceBancaire` contains no `where LocataireId = …`. The tenant is identified
  once, by `MiddlewareLocataire` (subdomain `beta.localhost:5114` or `X-Locataire` header), then read from `ContexteLocataire`.
- **The same URL, three answers**: `GET /comptes/1111…` gives Alice at €1,000, €3,000 or €25,000 depending on the bank.
  The primary key is composite (`LocataireId`, `Id`): the same identifiers coexist in the shared database.
- **The safeguard is in the DbContext**: a global filter on every entity implementing `IAppartientAUnLocataire`,
  stamping of inserts, an exception if a write targets another tenant. **Without a tenant, every query fails** instead
  of returning everything; the platform has to write `IgnoreQueryFilters()` to see several customers.
- **Two isolation models side by side** (`GET /plateforme/stockage`): Alpha and Beta (Standard) share `banque-partagee`,
  Gamma (Premium) has its own database `banque-gamma`. Only `FabriqueBanque` knows the difference.
- **Changing plans**: `POST /plateforme/locataires/alpha/formule` (Premium) moves Alpha's data into a dedicated database,
  the tenant being suspended during the copy.
- **Rules and quotas per customer**: transfer limit, fees, number of accounts (each bank's contract); requests per minute
  (plan). A **noisy neighbor** gets 429 without slowing the others down: one limiter per tenant, not a global one.
- **A background job has no tenant**: `POST /plateforme/taches/frais-mensuels` opens a scope per bank and sets the tenant
  itself. Forgetting it throws an exception, rather than charging fees across all the banks.
- **Customers arrive and leave** without a deployment: `POST /plateforme/locataires` then `DELETE` (database deleted in
  Premium, rows deleted in Standard).
- An account from another bank answers **404**, like a nonexistent account: its existence is not revealed.
- In production: the tenant comes from the authenticated user's **token** (or is checked against it), the catalog is a
  separate database, and schema migrations must run on **every** dedicated database. Common library: Finbuckle.MultiTenant.

## 16. Backend for Frontend (BFF) — `16-BFF`

```
Bff.Services  :5210  the generic business services (simulated in a single process): identity (tokens), profiles, accounts,
                     operations, advisors, offers; adjustable latency and failures (PUT /chaos)
Bff.Web       :5115  the SITE's BFF: serves the application (wwwroot), server-side session + HttpOnly cookie (the token
                     never goes to the browser), CSRF protection, a dashboard aggregated into one request
Bff.Mobile    :5116  the MOBILE APP's BFF: compact responses, one call per screen, cursor pagination,
                     /v1 and /v2 versions aligned with the app versions
```

```powershell
dotnet run --project 16-BFF\Bff.Services     # puis, dans deux autres terminaux :
dotnet run --project 16-BFF\Bff.Web          # http://localhost:5115 (alice / demo)
dotnet run --project 16-BFF\Bff.Mobile
```

Generic services serve several applications with opposite needs: a site on a big screen and a fast network, a mobile
app on a small screen and a slow network. Rather than make the services choose between the two, each application has
**its own backend**, written and deployed by its team, which acts as the interface between it and the services.

**What to look at** (details: [Docs/en/16-bff.md](Docs/en/16-bff.md); requests in `Bff.Web\Banque.http` and `Bff.Mobile\Banque.http`)
- **One screen, one request**: `GET /api/tableau-de-bord` (dashboard) triggers 7 calls to the services, made on the server
  and **in parallel**. The headers `X-Appels-Aval` (downstream calls), `X-Duree-Totale-Ms` (≈ 2 latencies) and
  `X-Duree-Aval-Cumulee-Ms` (≈ 7 latencies) show it.
- **The token stays on the server** (`Bff.Web\Session.cs`): the browser only receives an `HttpOnly`, `SameSite=Strict`
  cookie, pointing to a session kept by the BFF. The page shows `document.cookie = ""`: an injected script (XSS) has
  nothing to steal. Logging out deletes the session, and a cookie copied earlier is worthless.
- **CSRF**: every request to `/bff` and `/api` must carry `X-CSRF: 1`, which a form from another site cannot do.
- **No special privileges**: the BFF calls the services with **the user's** token; Alice cannot debit Bob's account,
  even through the BFF (403 from the services).
- **One response per screen**: the mobile app receives a response more than 5 times lighter than the site's, without IBAN
  or useless fields, with shortened labels ("CB CARREFOUR 0910 PARIS 15" → "Carrefour").
- **Degradation**: if the offers are down or too slow (800 ms budget), the dashboard is shown without them
  (`"degrade": ["offres"]`) instead of failing.
- **Versions**: `/mobile/v2/accueil` adds an offer banner for the new app version; `/v1` is still served to phones not
  yet updated. The services didn't change.
- **Two independent BFFs**: no code shared between them or with the services (checked by a test). The contracts are
  copied, with only the fields each screen needs.
- Not to be confused with an **API gateway** (demo 8): the gateway is single and generic (routing, security, quotas);
  a BFF is specific to one application. The two are often combined. In production: Duende BFF or YARP for forwarding,
  OpenID Connect for sign-in, a distributed cache for sessions.

## 17. Cell-Based Architecture — `17-CellBased`

```
Cell.Cellule   THE COMPLETE APPLICATION (API, data, rules), identical in every cell; each instance only serves its own
               customers. Run as c1 :5231, c2 :5232, c3 :5233 (+ c4 :5234 in reserve)
Cell.Routeur   :5117  DATA plane: partition key (the customer) → cell → forward, nothing else
                      CONTROL plane (/plateforme): placement, adding cells, migration, deployment in waves
Cell.Lanceur          starts the router and the cells (the same program, run three times)
```

```powershell
dotnet build 17-CellBased\CellBased.slnx
dotnet run --project 17-CellBased\Cell.Lanceur            # routeur + c1, c2, c3
dotnet run --project 17-CellBased\Cell.Lanceur -- c4      # plus tard : une 4e cellule, à enregistrer
```

Instead of a single system that keeps growing, you run **several complete, independent copies** of the application,
the **cells**, each serving part of the customers. A failure, a bad deployment or a greedy customer only reaches one
cell: the **blast radius** is bounded. An approach described by AWS for its own services, adopted notably by Slack,
and close to Salesforce's "pods".

**What to look at** (details: [Docs/17-cell-based.md](Docs/17-cell-based.md), French; requests in `Cell.Routeur\Banque.http`)
- **The router is tiny** (`Relais.cs`): it reads the customer from the URL, finds its cell in the placement table,
  forwards, and adds `X-Cellule`. No business logic, no database: it is the only shared component, it must be unbreakable.
- **Blast radius**: stop c2's process → Bob and Emma get 503, Alice, Carla and David notice nothing.
- **Placement**: a new customer goes into the least loaded open cell. When all are full (3 slots each), you **add** a
  cell (`POST /plateforme/cellules`) instead of growing one: they all keep a known, tested size.
- **Deployment in waves**: `POST /plateforme/deploiements` first updates **one** cell (the canary), checks its health
  probe, then the others. Version 1.1 (bug: a withdrawal debited twice) is stopped at the canary and rolled back; the
  version history shows that c2 and c3 never received it.
- **Migration**: `POST /plateforme/clients/carla/migrer` moves a customer (data, history, already processed operations)
  to another cell; that customer is unavailable for a moment (503 + `Retry-After`), the others are not.
- **The price: exchanges between cells.** A transfer between two customers of the same cell is a local transaction;
  between two cells, it is a debit, then an idempotent credit through the router, and a compensation if the
  beneficiary's cell is unreachable. Hence the importance of choosing the **partition key**: most operations must stay
  within one cell.
- **Control plane and data plane** are separate: if the control plane goes down, you can no longer place or deploy,
  but traffic continues with the existing placement table.
- Not to be confused with **multi-tenant** (demo 15), which shares one instance between customers; cells are often
  multi-tenant inside. Nor with **microservices** (demo 8), which split by function: a cell contains all the functions,
  possibly as microservices.

## 18. Actor Model — `18-ModeleActeurs`

```
Acteurs.Noyau    the RUNTIME, written for the demo (≈ 300 lines, no package): Acteur, ReferenceActeur (an address),
                 SystemeActeurs (one mailbox per actor, activation, supervision, passivation)
Acteurs.Banque   CompteActeur (one per account), VirementActeur (one per transfer), immutable messages,
                 MagasinEtats (durable storage, one row per actor), Pieges (failures and slowdowns on demand)
Acteurs.Api      :5118  one route = one message to an actor; /systeme to observe and trigger failures; /demo
```

Each account is an **actor**: an object that has its own state, shares it with no one, and only receives **messages**,
handled **one at a time** from its mailbox. No more locks or transactions to protect the balance: nobody else can touch
it. The model of Erlang, Akka and Orleans (Halo, Xbox); here, Orleans-style *virtual* actors, activated on the first
message and put to sleep after 30 s of inactivity.

**What to look at** (details: [Docs/18-modele-acteurs.md](Docs/18-modele-acteurs.md), French; requests in `Acteurs.Api\Banque.http`)
- **Concurrency without locks**: `POST /demo/retraits-simultanes` fires 1,000 €3 withdrawals **at the same time** on
  Alice's account: exactly 333 accepted, final balance €1, never negative. `CompteActeur` reads, checks and changes its
  balance like a sequential program.
- **Transfers without deadlocks**: one actor per transfer talks to both accounts (debit, credit, compensation if the target
  refuses). `POST /demo/virements-croises` fires simultaneous Alice → Bob and Bob → Alice transfers: the total is
  preserved. The transfer's identifier is its actor's address: replaying `facture-42` returns the same response.
- **"Let it crash"**: `POST /systeme/pannes` makes an actor crash at the worst moment (balance changed in memory, not yet
  written) → `500`, the supervisor **replaces** it with a fresh incarnation that rereads the storage: the withdrawal never
  happened. More than 3 crashes per minute → the actor is stopped (`503`); the other accounts notice nothing.
- **Transfer recovery**: crashed after the debit, it is marked `DebitFait` (debit done); replayed, it finishes without
  debiting twice.
- **Activation and passivation**: `GET /systeme` lists the actors in memory; `POST /systeme/passivation` puts them to
  sleep, their state stays in `/systeme/stockage`, and the next message only wakes up the actor concerned.
- **The price**: no `GET /comptes` (you would have to wake up and query every actor: you would add a read view, cf. CQRS);
  no transactions between actors; two actors waiting for each other deadlock.
- In production: **Orleans**, **Akka.NET** or **Proto.Actor**, which spread the actors over several servers.

## 19. Space-Based Architecture — `19-SpaceBased`

```
Espace.Grille    the VIRTUALIZED MIDDLEWARE, simulated (no package): data grid (one replica per unit, asynchronous
                 replication, versions, collision detection), messaging grid (affinity / round robin), data pump and
                 writer, data reader, deployment manager (elasticity)
Espace.Banque    a unit's business logic: it only reads and writes its own unit's replica
Espace.Api       :5119  /comptes, /virements; /grille to observe and trigger failures; /demo
```

The database is the bottleneck of every other demo. Here, it leaves the request path: each **processing unit** has
**all the data in memory**, in a grid replicated between the units; a **pump** writes to the database in the background.
Units are added when the load rises and removed when it drops. The model of GigaSpaces, Hazelcast or Apache Ignite
(ticketing, auctions, trading).

**What to look at** (details: [Docs/19-space-based.md](Docs/19-space-based.md), French; requests in `Espace.Api\Banque.http`)
- **The database can go down** (`POST /grille/base/panne`): requests still answer `200`, `pompeEnAttente` (pending pump)
  grows, then drains when the database comes back.
- **Collisions**, the price of the replicated grid: `POST /demo/retraits-concurrents` with **affinity** (each account
  always on the same unit) loses nothing; with **round robin** (`POST /grille/routage`), two units write the same account
  at the same time and accepted writes are lost (real run: 200 €1 withdrawals accepted, €100 debited).
- **Elasticity**: `POST /demo/charge` (600 simultaneous account openings) goes from 2 to 6 units, each one started by a
  simple memory copy of another; when things calm down, back to 2. The log (`GET /grille/journal`) tells the story.
- **Cold start**: `POST /grille/panne-totale` empties the grid; the next request waits for the pump to write everything,
  then reloads the grid from the database (`503` if the database is down too).
- `GET /grille/comparaison/{id}` shows one item in each replica and in the database, and the replication window.
- Even with affinity, a change in the number of units opens a short collision window (keys moving to another unit).

## 20. Pipeline (pipes & filters) — `20-Pipeline`

```
Pipeline.Noyau   the ENGINE, generic (no package): IFiltre, stages connected by bounded pipes (Channels),
                 ordered or unordered parallel stages, rejects by reason, metrics per stage, stop on failure
Pipeline.Banque  the filters for importing a file of transfers: Parse, Validate (IBAN), Deduplicate, Convert,
                 Check for fraud, Execute; and their assembly into two pipelines (import, dry-run import)
Pipeline.Api     :5120  POST /imports (the body is the CSV file, read as a stream); /demo/import-massif
```

Data processing split into independent **filters**, connected by **pipes**: the model of the Unix shell, compilers and
ETL. Here, importing a file of transfers sent by a company; each filter does one thing, and they all work at the same
time, each on a different line.

**What to look at** (details: [Docs/20-pipeline.md](Docs/20-pipeline.md), French; requests in `Pipeline.Api\Banque.http`)
- **Rejects**: the sample file (11 lines) gives 3 transfers and 8 rejects, each set aside by the filter concerned
  (malformed line, invalid IBAN, duplicate, unknown currency, blacklist, limit, insufficient balance).
- **Recomposition**: `POST /imports?aBlanc=true` is the same pipeline without the last filter: it tells what would go through.
- **Back-pressure**: `POST /demo/import-massif` imports 100,000 lines generated on the fly; never more than a few hundred
  in memory (`maxEnTransit`).
- **The bottleneck**: the report measures each stage; with 1 copy, the fraud check is 94% busy and the others wait.
  With 8 copies (`parallelismeFraude`), the import goes 5 times faster, and the lines come out in file order (order
  matters: a transfer can depend on the previous one).
- **Recovery**: `?panneALaReference=VIR-010` stops the import halfway (500); replaying the file finishes the job
  without executing anything twice (deduplication by reference).

## 21. Event-Driven Choreography — `21-Choregraphie`

```
Choreo.Bus       the BROKER, simulated (no package): one queue per subscriber, "at least once" delivery, retries,
                 dead letters, log of all events (correlation + cause)
Choreo.Services  the event contracts, and the services, which don't know each other: Comptes, Conformité, Change,
                 Notifications, Fidélité (loyalty, added afterwards), Suivi (tracking: the projection that rebuilds the transfers' state)
Choreo.Api       :5121  POST /virements (202); /conformite for the analyst; /bus, /chaos to observe and break things
```

The **same transfer as demo 14**, with the same data and the same rules, but **without an orchestrator**: each service
reacts to the others' events and publishes its own. The process is written nowhere; it emerges from the subscriptions.
Demos 7 and 8 used choreography to notify; here, it drives a whole business process.

**What to look at** (details: [Docs/21-choregraphie.md](Docs/21-choregraphie.md), French; requests in `Choreo.Api\Banque.http`)
- **The causal history**: `GET /virements/{id}` shows each event, who published it, in reaction to what. It is the
  process's only thread; "where is the transfer" requires one more service (the Tracking).
- **Scattered compensations**: a compliance refusal or an exchange failure is compensated by Accounts, which has to
  listen to both events.
- **A failure is silence**: `POST /chaos {"changeEnPanne": true}` → the transfer is left hanging, the event ends up in the
  dead letters (`/bus/lettres-mortes`), the transfer appears in `/virements/bloques`; 30 s later, Accounts releases the
  reservation on its own.
- **Delivered twice, handled once**: each service has its inbox (tested with a broker that duplicates everything).
- **Adding a participant**: the Loyalty service subscribes to `VirementRegle`, without any other service changing.
- `GET /bus`: who listens to what, the only map of the system, and it only exists at runtime.

## 22. Strangler Fig (progressive migration) — `22-StranglerFig`

```
Fig.Ancien   :5240  the system to REPLACE, written the old way (cents, dates without time zone, truncated labels); two
                    internal routes added for the migration: its change log and the import of the new system's operations
Fig.Nouveau  :5241  the REPLACEMENT: its own model, its own database, an anti-corruption layer, two-way synchronization,
                    and the legacy's historical contract reproduced exactly
Fig.Facade   :5122  the clients' only entry point: routing table (Legacy / Shadow / New) changeable live,
                    shadow-mode comparisons, migration tracking (/facade, header X-Cle-Admin: demo)
```

```powershell
dotnet run --project 22-StranglerFig\Fig.Ancien     # puis, dans deux autres terminaux :
dotnet run --project 22-StranglerFig\Fig.Nouveau
dotnet run --project 22-StranglerFig\Fig.Facade     # http://localhost:5122
```

Every other demo starts from scratch; this one starts from an **existing system**, which can be neither stopped nor
rewritten in one go. A façade is placed in front of it, then takes its features away one route at a time in favor of a
new service, until the day nothing calls it anymore. (In the code: *Ancien* = legacy, *Ombre* = shadow, *Nouveau* = new.)

**What to look at** (details: [Docs/22-strangler-fig.md](Docs/22-strangler-fig.md), French; requests in `Fig.Facade\Banque.http`)
- **Who answers?** The `X-Servi-Par` (served by) header of each response. At first, account lookup is migrated,
  operations are in shadow mode, the statement and transfers still on the legacy. Anything undeclared (`/agences`) goes
  to the legacy.
- **Live switch, both ways**: `PUT /facade/routes/virements {"mode": "Nouveau"}`, then `"Ancien"` to roll back. The legacy
  knows the transfers made in the meantime by the new system: it received them through synchronization.
- **Shadow mode**: the legacy answers, the new system is called in parallel and compared (`GET /facade/comparaisons`:
  identical, differences with both bodies, abandoned calls). The client never waits for the new system. Refused for a write.
- **Two-way synchronization**: the new system reads the legacy's log (including the fees of the nightly batch, which
  don't go through the façade) and writes its own changes back through an outbox; the echo of its writes doesn't come
  back twice, a resent batch is imported only once.
- **The legacy contract, quirks included**: `947.1` and not `947.10`, Paris time without a time zone, labels truncated
  to 30 characters, a statement identical down to the character. 20 contract tests send the same requests to both systems.
- **A visible limit**: right after a transfer served by the legacy, the lookup served by the new system shows the old
  balance, until the next synchronization (500 ms). Hence the advice to migrate the reads and writes of a piece of data together.
- **End of the migration**: `GET /facade/progression`; `ancienRetirable` (legacy removable) is only true if every route
  is migrated **and** no call reaches the legacy anymore. A single call to `/agences`, the forgotten feature, is enough
  to prevent it.

## 23. REST, gRPC, GraphQL (API styles) — `23-RestGrpcGraphQL`

```
Styles.Banque     the BUSINESS, shared by the three APIs, which knows nothing about HTTP (architecture test): single AND
                  batched reads, transfer, subscription to new operations, counter of "database" reads
Styles.Outillage  what Google.Protobuf, Grpc.AspNetCore and HotChocolate would provide, written by hand (no package):
                  Protobuf encoder, gRPC frames and trailers, gRPC client and hosting, mini GraphQL engine
Styles.Contrats   banque.proto and its classes (a test checks they match field by field)
Styles.Serveur    :5123  REST (/clients, /comptes, /virements) and GraphQL (POST /graphql), over HTTP/1.1
                  :5250  gRPC (banque.Banque/…), over HTTP/2 only
Styles.Client     the same application written three times; plays the same scenarios, counting requests and bytes
```

```powershell
dotnet run --project 23-RestGrpcGraphQL\Styles.Serveur    # puis, dans un autre terminal :
dotnet run --project 23-RestGrpcGraphQL\Styles.Client     # (-- --latence 0 pour ne pas simuler de réseau mobile)
```

The other demos change the inside of the system; this one keeps a single inside and changes its **surface**. What
varies: who decides the shape of the responses, what travels over the network, and what HTTP still does for you.

**What to look at** (details: [Docs/23-rest-grpc-graphql.md](Docs/23-rest-grpc-graphql.md), French; requests in `Styles.Serveur\Banque.http`)
- **Alice's home screen** (3 accounts and their latest operations): REST **5 requests** (the customer, their accounts,
  then one per account), GraphQL and gRPC **just one**. Bodies received: 2,014, 881 and 378 bytes; the same data, byte
  for byte.
- **The N+1 moves**: the single GraphQL query could read the database once per account; the engine executes level by
  level and the `operations` field has a batched resolver: one read (`GET /statistiques`).
- **One error, three languages**: `422` + `problem+json`; `200` + `grpc-status: 9` in the HTTP/2 **trailers**;
  `200` + `errors` next to the data obtained, with the field path and the propagation of non-null `null`s.
- **Push or poll**: the gRPC stream `SuivreOperations` receives each operation as soon as it happens, until the deadline
  (`grpc-timeout`, also enforced by the server); in REST, the **ETag** makes polling cheap (`304`).
- **Evolving**: Protobuf field numbers and unknown fields ignored; `@deprecated` in the GraphQL schema
  (`GET /graphql/schema`) and the counter of those still reading the field (`GET /graphql/utilisation`).
- **GraphQL protects itself**: query validated against the schema before execution, depth limited to 6 levels.

## 24. MVC and MVVM (presentation patterns) — `24-MvcMvvm`

```
Ihm.Banque        the MODEL, shared by both: asynchronous IBanque (adjustable latency), transfer rules; knows nothing about screens
Ihm.Mvc           :5124  web MVC (ASP.NET Core MVC): Controllers/, Views/ (Razor), Models/ (data prepared for each view)
Ihm.Presentation  MVVM: the ViewModels in pure .NET (no WPF, architecture test) + ObservableObject, Commande, CommandeAsync
Ihm.Wpf           the WPF view: only bindings, code-behind reduced to InitializeComponent (Windows)
Ihm.Console       a second view on the SAME ViewModels; Liaison.cs = a {Binding} taken apart in 20 lines
```

```powershell
dotnet run --project 24-MvcMvvm\Ihm.Mvc        # puis http://localhost:5124
dotnet run --project 24-MvcMvvm\Ihm.Wpf        # la fenêtre ; 400 ms par appel, pour voir l'écran attendre
dotnet run --project 24-MvcMvvm\Ihm.Console    # la même logique au clavier (« aide »)
```

The other demos organize the system or its API; this one organizes the **last mile**, between the model and the user.
What changes: who receives the action (the controller, or the view that passes it on to the ViewModel), and how long
the screen lives (one request, or as long as the window is open).

**What to look at** (details: [Docs/24-mvc-mvvm.md](Docs/24-mvc-mvvm.md), French; requests in `Ihm.Mvc\Banque.http`)
- **MVC**: the controller decides (which view, which data, which redirect), the view formats. Invalid form: the **same
  view shown again** (200) with the errors and the input, even "abc"; valid form: **Post/Redirect/Get** (302), message
  passed through **TempData** and shown only once; POST without a token: 400.
- **The confirmation** above €1,000: in MVC, **one more view** that sends back every field as hidden fields (the server
  kept nothing); in MVVM, an **awaited call** to `IConfirmation` in the middle of the command (a dialog box in WPF, a
  question at the keyboard in the console, a scripted answer in the tests).
- **MVVM**: each keystroke updates the ViewModel, which rechecks everything and notifies the view (`PropertyChanged`,
  `ErrorsChanged`, `CanExecuteChanged`): errors while typing, button enabled only when everything is right, source
  balance tracked. Real run: €1,200 from €1,000 is refused as you type in MVVM, but only after the confirmation page in
  MVC, which doesn't know the balance without calling the bank.
- **The screen lives, so it waits**: button greyed out during the call (no double transfer), responses arriving out of
  order discarded (click on Bob then on the Livret), balances updated in place without losing the selection.
- **Two views, same ViewModels**: the console shows the `⚠`, `»` and `[Virer] actif` lines on its own, pushed by the
  bindings. And the XAML bindings, which the compiler doesn't check, are checked by a test (30 paths followed).

---

## Comparison

| | Layered | Hexagonal | Onion | Clean | Functional Core | Vertical Slice | Components | Modular Monolith | Microservices | CQRS | Event Sourcing | Serverless | Microkernel | Orchestrated Saga | Multi-tenant | BFF | Cell-based | Actors | Space-based | Pipeline | Choreography | Strangler Fig | REST / gRPC / GraphQL | MVC / MVVM |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---| --- | --- | --- |
| Split | stacked technical layers | inside / outside | concentric layers | circles + one use case per class | pure / impure | by feature | by subdomain | by subdomain, autonomous modules | by subdomain, separate processes | write / read | decide / apply / project | by function triggered by an event | minimal core + plug-ins | an orchestrator, participants | by tenant (customer), at the data level | one backend per client application | by group of customers: N complete copies of the system | one actor per entity (account, transfer), private state | identical units, each with all the data in memory | by processing step: one filter per step | by service; the process emerges from their reactions | by route: legacy and new side by side behind a façade, for the duration of the migration | by API style: three thin façades over the same business | by role in the interface: controller and view (MVC), ViewModel and view (MVVM) |
| The business depends on… | the data layer | nothing | nothing | nothing | nothing | the DbContext | nothing (outside its component) | other modules' contracts | other services' JSON | nothing (write side) | nothing | the provider's managed services | nothing (plug-ins depend on the SDK) | the participants, via the orchestrator | nothing (the tenant is ambient) | downstream services (over HTTP) | nothing (each cell is autonomous) | nothing (an actor only knows addresses) | nothing (only its unit's replica) | nothing (a filter only knows its input and output) | event contracts, never other services | nothing (the legacy is only seen through the anti-corruption layer) | nothing (unaware of HTTP, JSON, Protobuf and GraphQL) | nothing (unaware of screens; the ViewModels are unaware of WPF) |
| What is stored | the state | the state | the state | the state | the state | the state | the state | the state | the state | the state + views | **the events** | the state, in managed services | the state | the state + each saga's state | the state, tagged by tenant (shared or dedicated database) | nothing, except sessions (the business is in the services) | the state, spread across cells (+ the placement table) | the state, one row per actor | the state, in memory in each unit; the database updated later | the state; the data only passes through | each service's state + the event log | the state, **twice** (legacy and new), synchronized both ways | the state | the state; in MVVM, the screen's state also lives in memory as long as the window is open |
| Projects | 4 | 6 | 5 | 5 | 3 | 2 | 4 | 8 | 7 | 5 | 5 | 4 (including the simulated platform) | 8 (including 4 plug-ins) | 4 | 4 | 4 (including the simulated services) | 4 (including the launcher) | 4 (including the runtime) | 4 (including the simulated middleware) | 4 (including the engine) | 4 (including the simulated broker) | 4 (including the legacy system) | 6 (including the hand-written tooling and the client) | 6 (including two MVVM views: WPF and console) |
| Deployments | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | **5** | 1 | 1 | **1 per function** (12) | 1 (+ plug-ins dropped in at runtime) | 1 orchestrator + N participants | **1 for all customers** | 1 per client application (+ the services) | 1 router + **N cells**, updated in waves | 1 (or a server cluster with Orleans, Akka) | **N units**, added and removed with the load | 1 | 1 per service + the broker | 3: façade, legacy, new; then 2 once the legacy is switched off | 1 (two ports: HTTP/1.1 for REST and GraphQL, HTTP/2 for gRPC) | 1 site (MVC); 1 application per machine (MVVM) |
| Abstractions | DAO defined by the data layer | ports | repository + UoW | boundaries + presenters + gateway | none in the core | none | 1 interface per component | contracts + events + `IModule` | HTTP API + messages | commands, queries, events, projections | event store, streams, projections | triggers + context + manifest | extension points (SDK) | steps: execute / compensate | tenant context, resolvers, database factory | copied contracts, per-screen views | partition key, placement table, control plane | actor, address, message, supervisor | data grid, messaging grid, pump, unit | filter, pipe, stage, report | event, subscription, correlation, cause, inbox | routing table, mode (legacy / shadow / new), change log, outbox | resources and representations; .proto contract and messages; schema and (batched) resolvers | controller, action, view, view data; ViewModel, binding, command, notifications |
| Communication | direct calls | port calls | direct calls | boundaries | direct calls | direct calls | interface | interface **and** events | HTTP **and** message broker | events (write → read) | event streams | events: HTTP, queues, storage, timer | calls from the core to the plug-ins | orchestrator commands + external events | direct calls | HTTP to the services, in parallel | router → cell; between cells, rare and idempotent | messages: tell (no reply) or ask (with a timeout) | asynchronous replication between units; pump to the database | bounded pipes between filters, streaming | events only, published without knowing the subscribers | HTTP: façade → one or the other; synchronization through the log (legacy → new) and import (new → legacy) | REST: HTTP verbs and JSON; gRPC: HTTP/2, Protobuf, streaming; GraphQL: POST /graphql and JSON | MVC: request → controller → view → HTML; MVVM: two-way bindings and commands |
| Typical tests | service + fake DAO (persistence entities) | unit + test adapters | unit with fakes | interactor + output spy | pure functions, no mocks | HTTP integration | black box per component | cross-module journeys + isolated module | each service alone, fake HTTP servers | events produced / views obtained from events | Given / When / Then on events | functions called like methods + full system | core + in-memory plug-ins, real DLLs, unloading | manual clock: retries, deadlines, recovery | isolation: a tenant neither sees nor changes another; quotas | BFF + real in-memory services: aggregation, security, degradation | full in-memory system: blast radius, waves, migration | instrumented toy actor: 1,000 simultaneous messages, failures, passivation | replication held back by hand: guaranteed collisions; elasticity, cold start | filters one by one; hand-assembled test pipelines; order, back-pressure, recovery | chain of causes; a broker that duplicates everything; manual clock for expiry; architecture test | contract tests (same requests to both systems); broken links or links that lose responses; shadow mode | the three clients against the same server: same result, requests and bytes counted; wire format byte by byte; .proto compared to the classes | controller called like a method; in-memory site (HTML, token, PRG); ViewModels without a screen, with a bank that holds calls back |
| Safeguard | non-transitive references | discipline (+ architecture test) | project references | project references | separate projects | discipline | **compiler** (`internal`) | `internal` + Contracts projects + architecture test | **the network**: impossible to cheat | project references (+ architecture test) | append-only store + versions | **the platform**: no state possible between two calls | one load context per plug-in | the engine: state saved at every step | global filter + stamping + composite key (+ architecture test) | no references between BFFs (architecture test); permissions checked by the services | **the cell**: a failure or a bug doesn't leave it | **the mailbox**: one message at a time; the supervisor replaces the faulty actor | versions (the highest wins) + affinity routing | types checked at assembly; bounded pipes (back-pressure) | no references between services (architecture test); local expiry timeouts | the façade: anything undeclared stays on the legacy; rollback in one request; shadow mode refused for writes | the .proto (matching test); the GraphQL schema, validated before execution; architecture test (business without HTTP) | MVC: anti-forgery token, ModelState; MVVM: ViewModels without WPF (architecture test), XAML bindings checked by a test |
| Hidden cost | — | — | — | ceremony | — | possible duplication | — | — | network failures, idempotency, eventual consistency, operations | duplicated data, eventual consistency, projections to maintain | immutable events to version, GDPR erasure, learning curve | cold starts, idempotency everywhere, provider lock-in, distributed debugging | SDK to version, plug-ins in-process | idempotent participants, an orchestrator to operate | leaks between customers at the slightest slip, noisy neighbor, migrations on N databases | one more component per application, duplication between BFFs, temptation to put business logic in it | cross-cell exchanges, spread data, N times the infrastructure, customer migrations | no transactions between actors, cross-cutting reads, request cycles, asynchronous debugging | collisions (lost writes), everything in memory, database lagging, operating the grid | streaming processing only; single stateful filters; order to preserve; idempotent recovery | process written nowhere; scattered compensations; silent failures; tracking projection to maintain | two systems to run together; synchronization (loops, failures, lag); legacy contract to reproduce; a migration that drags on | REST: round trips, over-fetching; gRPC: HTTP/2, binary, no browser; GraphQL: hidden N+1, queries to bound, no more HTTP cache | MVC: validation after submission, one page per step; MVVM: binding engine, uncompiled bindings, subscriptions and stale responses |

The progression 6 → 7 → 8 shows the same split by subdomain, ever more isolated: `internal` visibility, then autonomous
modules in one process, then services separated by the network. Each step brings independence (teams, deployments,
scaling) and costs complexity. You only move to the next one if that need is real: **a well-split modular monolith is
the recommended starting point**, and its modules become microservices the day it is needed.

These approaches combine, and demo 7 is the example: a **modular monolith** at the macro level, then, inside each module,
the architecture that fits (**vertical slices** for a CRUD module, **hexagonal** or **functional core** for a module with
rich rules), with **ports & adapters** around external services. **CQRS** sits on top of any of them, locally, in the
module whose reads justify it (here: statements, history, dashboard). **Event Sourcing** goes one notch further, and only
for the modules where history is itself business data.

**DDD** has no column in the table: it is not a competing architecture, but the approach that says **where to cut**. Its
bounded contexts give the modules (demo 7) or the services (demo 8), and its aggregates give the event streams (demo 10).
Its value objects and its language improve any of the other demos.

**Multi-tenant** (demo 15) is cross-cutting too: it doesn't say how to organize the code, but how to **share** an
application between several customers without mixing them, whatever its internal organization. It combines with the
**Microkernel** when customers want different behaviors (and not just settings), and with **microservices**, where each
service must then propagate the tenant and apply it to its own data.

The **BFF** (demo 16) sits in front of any of these systems, on the client side: when several applications (site,
mobile, partners) consume the same services with different needs, each gets a backend tailored to it. It naturally
extends **microservices** (demo 8), whose calls it aggregates, and coexists with their gateway.

The **cell-based architecture** (demo 17) is the last notch of isolation: after modules, services and functions, you
duplicate the **whole** system so that each copy only serves part of the customers. It sits above any internal
architecture (a cell can be a modular monolith or microservices) and is only justified at large scale, when a global
outage costs more than N times the infrastructure.

The **actor model** (demo 18) answers another question: not how to split or deploy, but **how to handle concurrency**.
Instead of protecting shared state (transactions, locks), it shares none: each entity is an actor, owner of its state.
It lives inside a service or a module, combines with **Event Sourcing** (an actor can store its events rather than its
state, like Akka Persistence) and with **CQRS** for the cross-cutting reads it makes hard; a process between actors is
written like a small **saga** (demo 14).

The **space-based architecture** (demo 19) tackles the last thing all the others have in common: the database on the
path of every request. By replacing it with an in-memory grid, it gains elasticity and resilience to peaks, and pays in
consistency (possible collisions, database lagging). It shares with **actors** the idea of giving each piece of data a
single owner (affinity routing), and with **CQRS** a read database updated later.

The **pipeline** (demo 20) doesn't organize an application but a **processing job**: import, export, ETL, streams. It
lives inside a service or a module organized according to any other demo, and reuses at its own scale ideas seen
elsewhere: independent steps like **vertical slices**, the **back-pressure** of message queues, the **idempotency**
that allows replaying (demos 8 and 14).

**Choreography** (demo 21) closes the loop opened by demo 14: the same transfer, without a conductor. The comparison is
clear-cut for a process of this size: choreography decouples the services and makes adding a reaction trivial, but
scatters compensation and timeouts, and turns a failure into silence. Hence the common practice: **orchestrate
processes, choreograph reactions**.

The **Strangler Fig** (demo 22) doesn't pick an architecture, it says **how to get there** from an existing system:
the target can be any of the previous demos. It reuses many of their ideas: a façade like the microservices' **gateway**
(demo 8), an **anti-corruption layer** like DDD (demo 11), an **outbox**, **idempotency** and **eventual consistency**
as everywhere two databases must agree. This is often how a monolith becomes modular or splits into services: one
route, then another, with no D-day.

The **REST, gRPC, GraphQL** demo (23) finally looks at the system's **surface**, which all the others exposed in REST
without saying so. The API style changes nothing in the business (a test checks it) but everything in what the client
pays: round trips, bytes, errors, streaming. It is chosen **per boundary** and combined: **gRPC between the services** of
demos 8, 14 or 17, where throughput and strict contracts matter; **REST** for a public API, where HTTP caching and
simplicity matter; **GraphQL** in front of rich and varied screens, where you would otherwise write one **BFF** per
application (demo 16), with which it shares the idea of a response tailored to the screen.

The **MVC and MVVM** demo (24) goes down to the **last mile**, the interface, which all the others left to `Banque.http`
files. It doesn't compete with the others: the model stays behind, organized as you like, and MVC or MVVM is only demo
0's **presentation layer**, split in turn. The choice follows the platform: **MVC** for a server-rendered site, where
each click is a stateless request; **MVVM** for a desktop or mobile application, whose screen lives and reacts to every
keystroke, and can be fully tested without a screen. Both combine with the **BFF** (demo 16): one backend per
interface, each organized according to its platform.

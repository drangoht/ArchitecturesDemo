# Software architectures, explained to everyone

🌐 [Français](../../pour-tous/README.md)

You don't need to know how to program to read these pages. With simple words and everyday images, they explain the
**25 ways of organizing an application** presented in this repository. For the technical details (code, diagrams,
tests), each page links to its version for developers.

<p align="center"><img src="../../pour-tous/images/accueil.jpg" alt="Illustration: a city made of very different buildings, connected by roads" width="480"></p>

📘 The slightly technical words are explained in the [short glossary](lexique.md).

---

## An application is like a building

An application is a set of rules (here: those of a small bank), data to keep (the accounts, the balances) and screens
so that people can use it. All of this can be arranged in a thousand ways, just as you can organize a house, a
restaurant or a factory.

No way is "the best". Each one solves a precise problem, and sometimes creates another. The right choice depends on the
size of the project, the number of people working on it, the number of users, and what costs the most when it breaks.

To compare, **all the demos do the same thing**: a small bank where **Alice transfers €250 to Bob**. Only the way things
are arranged changes.

---

## Six different questions

These architectures don't all answer the same question. They can be combined, one per question.

### ① How to arrange the code inside an application?

*Like organizing the rooms of a house.*

| # | Name | French name | In one picture |
|---|---|---|---|
| 0 | [Layers](00-couches.md) | Architecture en couches | A restaurant: the dining room, the kitchen, the storeroom, each on its own floor |
| 1 | [Hexagonal](01-hexagonale.md) | Architecture hexagonale | A laptop and its sockets: you plug in whatever you want around it |
| 2 | [Onion](02-onion.md) | — | An onion: the most precious part in the center, protected by layers |
| 3 | [Clean Architecture](03-clean.md) | — | A town hall: one counter per procedure |
| 4 | [Functional Core](04-coeur-fonctionnel.md) | — | The judge who decides on paper, the bailiff who goes into the field |
| 5 | [Vertical Slice](05-vertical-slice.md) | — | A recipe book: everything needed for a dish on the same card |
| 13 | [Microkernel](13-microkernel.md) | — | A drill and its interchangeable bits |
| 20 | [Pipeline](20-pipeline.md) | — | An assembly line |

### ② How to split a large system into pieces?

*Like organizing a company: one building or several, departments, managers.*

| # | Name | French name | In one picture |
|---|---|---|---|
| 6 | [Package by Component](06-composants.md) | — | A company's departments, each with its own reception desk |
| 7 | [Modular Monolith](07-monolithe-modulaire.md) | Monolithe modulaire | A shopping mall: independent shops under one roof |
| 8 | [Microservices](08-microservices.md) | — | A village of small shops, each in its own house |
| 12 | [Serverless](12-serverless.md) | — | A taxi rather than a car: you pay per ride |
| 14 | [Orchestrated Saga](14-saga-orchestree.md) | Saga orchestrée | A wedding planner who calls each supplier |
| 15 | [Multi-tenant](15-multi-tenant.md) | — | An apartment building: one building, one locked apartment per customer |
| 16 | [Backend for Frontend](16-bff.md) | — | A different interpreter for each visitor |
| 17 | [Cells](17-cell-based.md) | Architecture cellulaire | Identical greenhouses: a broken pane only freezes one |
| 18 | [Actors](18-modele-acteurs.md) | Modèle d'acteurs | Counter clerks who each have their own mailbox |
| 19 | [Space-based](19-space-based.md) | Architecture spatiale | Supermarket checkouts opened when the crowd arrives |
| 21 | [Choreography](21-choregraphie.md) | Chorégraphie | Dancers who coordinate without a conductor |

### ③ How to represent the rules and the data?

| # | Name | French name | In one picture |
|---|---|---|---|
| 9 | [CQRS](09-cqrs.md) | — | The kitchen on one side, the specials board on the other |
| 10 | [Event Sourcing](10-event-sourcing.md) | — | The account book: you write down every operation, never just the total |
| 11 | [Domain-Driven Design](11-ddd.md) | — | An architect who learns the residents' language before drawing |

### ④ How to replace an old system without stopping everything?

| # | Name | French name | In one picture |
|---|---|---|---|
| 22 | [Strangler Fig](22-strangler-fig.md) | — | Renovating your house room by room, while still living in it |

### ⑤ How do other applications talk to the system?

| # | Name | French name | In one picture |
|---|---|---|---|
| 23 | [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) | — | Three ways to order: à la carte, on a coded slip, or by ticking a list |

### ⑥ How to organize the screen the user sees?

| # | Name | French name | In one picture |
|---|---|---|---|
| 24 | [MVC and MVVM](24-mvc-mvvm.md) | — | Mail you send and receive, or a dashboard that moves live |

---

## Where to start?

1. [Layers](00-couches.md): the classic way, the one whose flaw all the others try to fix.
2. [Hexagonal](01-hexagonale.md): the big idea that follows, protecting the rules from everything else.
3. [Modular Monolith](07-monolithe-modulaire.md) then [microservices](08-microservices.md): the most debated choice
   today, one building or several.
4. Then, as you like, whatever intrigues you.

And if you had to remember only one sentence: **start simple, and only make things more complex when a real problem
demands it.** Every more elaborate architecture has a price, in time, money and complexity.

🔧 The full technical documentation: [Docs](../README.md).

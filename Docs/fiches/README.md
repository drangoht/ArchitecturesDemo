# Fiches mémo : une page par architecture

Le résumé technique de chaque démo, à relire en deux minutes : l'idée en une phrase, un schéma, les règles qui font
l'architecture, quand l'utiliser ou non, les pièges. Pour le code, les tests et les déroulements, chaque fiche renvoie
à sa page complète.

🖨️ **Version imprimable** : [fiches-architectures.pdf](pdf/fiches-architectures.pdf) (A4, une fiche par page,
27 pages), régénérée par `python Docs/fiches/pdf/generer-pdf.py` (Chrome ou Edge, connexion à jsDelivr).

📖 Documentation complète : [Docs](../README.md) · 📗 Version sans code : [pour tous](../pour-tous/README.md) ·
★ [Concepts transverses](../concepts-transverses.md) (idempotence, outbox, concurrence optimiste, compensation)
· ⚖️ [Les différences, deux à deux](../differences/README.md)

---

## ① Organisation du code : où mettre quoi, qui dépend de qui ?

| # | Fiche | Le cœur de l'idée |
|---|---|---|
| 0 | [Couches](00-couches.md) | Les dépendances descendent vers la base |
| 1 | [Hexagonale](01-hexagonale.md) | Ports et adapters ; tout pointe vers le métier |
| 2 | [Onion](02-onion.md) | La même règle, avec un centre découpé en couches |
| 3 | [Clean](03-clean.md) | Un interactor par cas d'utilisation, un presenter en sortie |
| 4 | [Cœur fonctionnel](04-coeur-fonctionnel.md) | Décider en pur, appliquer dans la coquille |
| 5 | [Vertical Slice](05-vertical-slice.md) | Un fichier par fonctionnalité |
| 13 | [Microkernel](13-microkernel.md) | Un noyau, des plugins chargés à chaud |
| 20 | [Pipeline](20-pipeline.md) | Des filtres en parallèle, des tuyaux bornés |

## ② Découpage du système : quelles frontières, quel déploiement ?

| # | Fiche | Le cœur de l'idée |
|---|---|---|
| 6 | [Composants](06-composants.md) | Une interface publique, le reste `internal` |
| 7 | [Monolithe modulaire](07-monolithe-modulaire.md) | Des modules par contrats, un seul déploiement |
| 8 | [Microservices](08-microservices.md) | Un processus et une base par service |
| 12 | [Serverless](12-serverless.md) | Des fonctions sans état, déclenchées par des événements |
| 14 | [Saga orchestrée](14-saga-orchestree.md) | Un orchestrateur conduit et compense |
| 15 | [Multi-tenant](15-multi-tenant.md) | Une application, plusieurs clients isolés |
| 16 | [BFF](16-bff.md) | Un backend par application cliente |
| 17 | [Cellules](17-cell-based.md) | N copies complètes, rayon d'impact borné |
| 18 | [Acteurs](18-modele-acteurs.md) | État privé, messages un par un, aucun verrou |
| 19 | [Spatiale](19-space-based.md) | Les données en mémoire, la base en différé |
| 21 | [Chorégraphie](21-choregraphie.md) | Chacun réagit aux événements des autres |

## ③ Modélisation : comment représenter le métier et l'état ?

| # | Fiche | Le cœur de l'idée |
|---|---|---|
| 9 | [CQRS](09-cqrs.md) | Un modèle pour écrire, un autre pour lire |
| 10 | [Event Sourcing](10-event-sourcing.md) | On stocke les événements, l'état se rejoue |
| 11 | [DDD](11-ddd.md) | La langue du métier, des contextes délimités |

## ④ Migration · ⑤ Exposition · ⑥ Présentation

| # | Fiche | Le cœur de l'idée |
|---|---|---|
| 22 | [Strangler Fig](22-strangler-fig.md) | Une façade, une route migrée à la fois, réversible |
| 23 | [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) | Ressources, procédures, graphe choisi par le client |
| 24 | [MVC et MVVM](24-mvc-mvvm.md) | Une vue jetée à chaque requête, ou un ViewModel vivant |

---

## Trois réflexes communs à toutes les fiches

1. **Commencer simple** ([Couches](00-couches.md), [Vertical Slice](05-vertical-slice.md),
   [Monolithe modulaire](07-monolithe-modulaire.md)) et ne compliquer que face à un vrai problème.
2. **Faire respecter l'architecture** par le compilateur (`internal`, références de projets) ou par un test
   d'architecture, sinon elle finit contournée.
3. **Dès qu'il y a un réseau ou des messages** : idempotence, outbox, cohérence à terme, compensation
   ([concepts transverses](../concepts-transverses.md)).

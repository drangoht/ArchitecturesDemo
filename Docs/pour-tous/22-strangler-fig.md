# 22. Le figuier étrangleur (« Strangler Fig », remplacer pas à pas)

🏠 [Sommaire](README.md) · ⬅️ [La chorégraphie](21-choregraphie.md) · ➡️ [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) · 🔧 [Version technique](../22-strangler-fig.md)

<p align="center"><img src="images/22.jpg" alt="Illustration : rénover sa maison pièce par pièce, en continuant d'y habiter" width="480"></p>

> **En une image** : rénover sa maison pièce par pièce, en continuant d'y habiter. On refait la cuisine, puis la salle
> de bains, puis les chambres ; la famille passe d'une pièce à l'autre sans jamais dormir dehors. À la fin, il ne reste
> plus rien de l'ancienne maison.

---

## L'idée

Le nom vient d'un arbre tropical, le figuier étrangleur : il pousse autour d'un vieil arbre, l'enveloppe peu à peu, et
finit par le remplacer complètement.

Toutes les autres pages partent de zéro. Ici, on part d'un **vieux système**, en service, qu'on ne peut ni arrêter ni
réécrire d'un coup. Réécrire tout en secret pendant deux ans, puis basculer un lundi matin : c'est le scénario des
grandes catastrophes informatiques.

À la place :

1. on place un **accueil** (une façade) devant l'ancien système : tous les utilisateurs passent par lui ;
2. on réécrit **une fonction à la fois** dans le nouveau système ;
3. l'accueil envoie cette fonction vers le nouveau, le reste vers l'ancien ;
4. au moindre problème, on **revient en arrière** en un instant ;
5. quand plus rien n'appelle l'ancien système, on l'éteint.

On peut même tester en **mode ombre** : l'ancien répond aux utilisateurs, le nouveau reçoit la même demande en coulisse,
et l'on compare leurs réponses sans prendre aucun risque.

## Pourquoi c'est bien

- **Pas de « grand soir »** : chaque étape est petite, réversible et utile tout de suite.
- **Le risque est faible** : on prouve que le nouveau fait comme l'ancien avant de lui confier les clients.
- **On peut s'arrêter en route** et garder un mélange qui fonctionne.

## Ce que ça coûte

- **Deux systèmes à faire vivre ensemble**, parfois longtemps, et à garder synchronisés : ce que l'un écrit, l'autre
  doit le savoir.
- **Il faut reproduire les habitudes de l'ancien**, bizarreries comprises, tant que d'autres en dépendent.
- **Le risque de ne jamais finir** : la dernière fonction oubliée empêche d'éteindre l'ancien.

## Quand la choisir

- Pour **remplacer un système existant** qu'on ne peut pas arrêter. Presque toujours, en fait, dès qu'il est en
  production.
- C'est souvent ainsi qu'une application devient modulaire, ou se découpe en microservices : une fonction après l'autre.

## Dans notre banque

Un ancien système, écrit « à l'ancienne », et un nouveau. L'accueil indique, dans chaque réponse, qui a répondu. La
consultation est déjà passée au nouveau, les opérations sont en mode ombre, les virements encore à l'ancien ; on les
bascule, puis on revient en arrière d'une simple demande. Et l'on ne peut éteindre l'ancien que lorsque toutes les
fonctions sont migrées et que plus personne ne l'appelle : une petite fonction oubliée, la liste des agences, suffit à
l'empêcher.

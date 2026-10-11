# 23. REST, gRPC, GraphQL (trois façons de parler à une application)

🏠 [Sommaire](README.md) · ⬅️ [Le figuier étrangleur](22-strangler-fig.md) · ➡️ [MVC et MVVM](24-mvc-mvvm.md) · 🔧 [Version technique](../23-rest-grpc-graphql.md)

<p align="center"><img src="images/23.jpg" alt="Illustration : trois façons de passer commande au restaurant" width="480"></p>

> **En une image** : trois façons de passer commande au restaurant. **À la carte** : un plat par commande, et l'on
> recommence pour le dessert. **Sur un bon de commande codé**, entre professionnels : rapide et sans ambiguïté, à
> condition d'avoir le même carnet. **En cochant une liste** : je coche exactement ce que je veux, tout arrive d'un coup.

---

## L'idée

Les autres pages organisent l'intérieur de l'application. Celle-ci regarde **comment les autres programmes lui parlent**
(une application mobile, un site, un partenaire) : son **API**, son guichet.

- **REST** (le plus répandu) : l'application présente des **choses** (un client, ses comptes, les opérations d'un
  compte), chacune à son adresse. On les demande une par une. Simple, compris par tous les outils, et le navigateur peut
  garder les réponses en mémoire pour ne pas redemander.
- **gRPC** (venu de Google) : on appelle des **actions** (« donne-moi l'écran d'accueil d'Alice »), dans un format
  binaire, très compact, décrit dans un contrat strict partagé par les deux côtés. Très rapide, idéal entre serveurs,
  mais un navigateur ne sait pas le parler directement.
- **GraphQL** (venu de Facebook) : l'application est vue comme un **réseau d'informations reliées**, et c'est le client
  qui dit **exactement** ce qu'il veut, en une seule demande : « le nom d'Alice, ses comptes, et les trois dernières
  opérations de chacun ».

## Pourquoi c'est bien

- **REST** : simple, universel, facile à tester ; les réponses peuvent être gardées en cache.
- **gRPC** : très rapide et léger ; contrat strict ; le serveur peut envoyer des nouvelles en continu.
- **GraphQL** : un aller-retour par écran, rien de superflu ; idéal pour des applications aux écrans riches et variés.

## Ce que ça coûte

- **REST** : trop d'allers-retours pour un écran complexe, ou trop d'informations inutiles.
- **gRPC** : illisible sans outils, pas directement utilisable depuis un navigateur.
- **GraphQL** : plus difficile à protéger (une demande peut être très gourmande) et à mettre en cache ; un moteur de
  plus à faire tourner.

## Quand la choisir

On choisit **pour chaque guichet**, et l'on combine souvent les trois :

- **REST** pour une API publique ou pour des partenaires ;
- **gRPC** entre les services internes d'un même système ;
- **GraphQL** devant des applications mobiles et web aux écrans riches.

## Dans notre banque

La même banque, offerte trois fois. Pour l'écran d'accueil d'Alice (trois comptes et leurs dernières opérations), REST a
besoin de cinq demandes, GraphQL et gRPC d'une seule. Pour exactement les mêmes informations, la réponse REST pèse
environ 2 000 octets, la réponse GraphQL 900, et la réponse gRPC moins de 400.

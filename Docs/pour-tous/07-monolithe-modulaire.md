# 7. Le monolithe modulaire

🏠 [Sommaire](README.md) · ⬅️ [Les composants](06-composants.md) · ➡️ [Les microservices](08-microservices.md) · 🔧 [Version technique](../07-monolithe-modulaire.md)

<p align="center"><img src="images/07.jpg" alt="Illustration : un centre commercial" width="480"></p>

> **En une image** : un centre commercial. Chaque boutique a sa vitrine, sa caisse, son stock et son personnel ; elles
> partagent le même toit, la même électricité et le même parking. Une boutique peut ouvrir ou fermer sans que les autres
> déménagent.

---

## L'idée

« Monolithe » veut dire : **une seule application**, installée d'un bloc. Le mot a mauvaise réputation, parce qu'on
l'associe au plat de spaghettis où tout est mélangé. Ici, c'est l'inverse : à l'intérieur, l'application est divisée en
**modules** très indépendants.

Chaque module est une petite application complète : il a ses propres données, ses propres pages, ses propres règles.
Les modules se parlent de deux façons :

- **en se demandant quelque chose**, par un contrat écrit (« débite ce compte et dis-moi si ça a marché ») ;
- **en annonçant ce qui s'est passé** (« un virement vient d'avoir lieu »), sans savoir qui écoute.

On peut ajouter un module « notifications » qui écoute les annonces, sans toucher aux autres.

## Pourquoi c'est bien

- **La simplicité d'une seule application** : une installation, un seul endroit à surveiller, des échanges instantanés
  et fiables entre modules.
- **La propreté d'un découpage fort** : chaque équipe a son module, ses données, ses règles.
- **Une porte ouverte vers l'avenir** : le jour où un module a vraiment besoin de vivre seul, il est prêt à être sorti
  et à devenir un microservice.

## Ce que ça coûte

- **Tout le monde partage le même sort** : si l'application tombe, tous les modules tombent ; on la met à jour d'un bloc.
- Chaque module ayant ses données, une opération à cheval sur deux modules doit prévoir comment se rattraper en cas
  d'échec.
- Il faut tenir les frontières dans la durée : la tentation de « juste lire une donnée du voisin » est permanente.

## Quand la choisir

- **C'est le point de départ recommandé** pour la plupart des projets un peu ambitieux : on découpe proprement, sans payer
  tout de suite le prix des microservices.
- Quand plusieurs équipes travaillent sur la même application.

## Dans notre banque

Trois modules : comptes, virements, notifications. Les virements demandent aux comptes de débiter et de créditer, puis
annoncent « virement effectué ». Les notifications écoutent cette annonce et préviennent les clients. Retirer le module
des notifications tient en une ligne, et rien d'autre ne casse.

# 18. Le modèle d'acteurs

🏠 [Sommaire](README.md) · ⬅️ [Les cellules](17-cell-based.md) · ➡️ [L'architecture spatiale](19-space-based.md) · 🔧 [Version technique](../18-modele-acteurs.md)

<p align="center"><img src="images/18.jpg" alt="Illustration : un bureau de poste où chaque guichetier a sa propre boîte aux lettres et ses propres dossiers" width="480"></p>

> **En une image** : un bureau de poste où chaque guichetier a sa propre boîte aux lettres et ses propres dossiers. Il
> traite son courrier **une lettre à la fois**, et personne d'autre ne touche à ses dossiers. Si un guichetier tombe
> malade, un remplaçant prend sa place et reprend ses dossiers là où ils étaient rangés.

---

## L'idée

Le cauchemar classique : deux demandes arrivent **en même temps** sur le même compte. Chacune lit « 1 000 € », chacune
retire, et l'une des deux opérations disparaît. Les solutions habituelles (verrous, transactions) sont délicates et
ralentissent tout.

Le modèle d'acteurs prend le problème à l'envers : chaque compte est un **acteur**, un petit personnage qui :

- **possède son propre état** (son solde), que personne d'autre ne peut toucher ;
- ne reçoit que des **messages** (« retire 3 € », « donne-moi ton solde ») ;
- les traite **un par un**, dans l'ordre d'arrivée.

Plus de bousculade possible : il n'y a jamais deux mains dans le même tiroir. Et quand un acteur plante, un
**superviseur** le remplace par un neuf, qui relit l'état sauvegardé.

## Pourquoi c'est bien

- **La concurrence devient simple** : chaque acteur raisonne comme s'il était seul au monde.
- **Des millions d'acteurs** peuvent exister ; ceux qui ne servent pas sont endormis, et réveillés au premier message.
- **Les pannes restent locales** : un acteur qui plante est remplacé, les autres continuent.

## Ce que ça coûte

- **Pas de « tout ou rien » entre deux acteurs** : un virement devient une conversation (débite, puis crédite, et rends
  l'argent si la suite échoue).
- **Les questions transversales sont difficiles** : « la liste de tous les comptes » oblige à interroger chaque acteur.
- **Deux acteurs qui s'attendent mutuellement** peuvent rester bloqués.

## Quand la choisir

- Quand on a **beaucoup d'objets indépendants**, souvent modifiés en même temps : comptes, joueurs d'un jeu en ligne,
  objets connectés, paniers.
- C'est le modèle de la messagerie WhatsApp (Erlang) ou des serveurs de jeux Halo (Orleans).

## Dans notre banque

Un acteur par compte, un acteur par virement. Mille retraits de 3 € arrivent **en même temps** sur le compte d'Alice
(1 000 €) : exactement 333 sont acceptés, il reste 1 €, et le solde n'est jamais négatif. On fait planter un acteur au
pire moment : il est remplacé, et le retrait à moitié fait n'a tout simplement pas eu lieu.

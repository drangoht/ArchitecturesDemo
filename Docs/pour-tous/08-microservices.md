# 8. Les microservices

🏠 [Sommaire](README.md) · ⬅️ [Le monolithe modulaire](07-monolithe-modulaire.md) · ➡️ [CQRS](09-cqrs.md) · 🔧 [Version technique](../08-microservices.md) · 🌐 [English](../en/pour-tous/08-microservices.md)

<p align="center"><img src="images/08.jpg" alt="Illustration : un village de petites boutiques, chacune dans sa propre maison" width="480"></p>

> **En une image** : un village de petites boutiques, chacune dans sa propre maison. Elles ont leurs horaires, leur
> personnel, leur stock. Pour travailler ensemble, elles s'appellent au téléphone et s'envoient des colis par coursier.

---

## L'idée

On reprend les modules du monolithe modulaire, et on donne à chacun **sa propre maison** : un programme séparé, qui tourne
seul, avec sa propre base de données. Les services ne partagent rien ; ils se parlent uniquement **par le réseau**, soit
en s'appelant (« débite ce compte »), soit en s'envoyant des messages (« un virement a eu lieu »).

Une seule porte d'entrée, la **passerelle**, accueille les clients et les dirige vers le bon service, comme l'office de
tourisme à l'entrée du village.

## Pourquoi c'est bien

- **Chaque service vit sa vie** : on peut le mettre à jour, le redémarrer ou le renforcer sans toucher aux autres.
- **Chaque équipe a sa boutique**, et peut avancer à son rythme, avec ses propres outils.
- **On renforce seulement ce qui en a besoin** : si les virements sont très demandés, on ajoute des virements, pas tout
  le reste.
- **Une panne reste locale** : si le service des notifications tombe, les virements continuent.

## Ce que ça coûte

C'est le grand piège de cette architecture : **tout ce qui était facile devient difficile**.

- **Le réseau n'est pas fiable.** Un appel peut échouer, arriver en retard, ou réussir sans que la réponse revienne. Il
  faut réessayer, sans jamais débiter deux fois.
- **Plus de « tout ou rien »** : si le débit réussit et que le crédit échoue, il faut rendre l'argent soi-même.
- **Les informations sont parfois en retard** : la notification arrive quelques secondes après le virement.
- **Beaucoup plus de choses à surveiller** et à faire tourner, avec des pannes plus difficiles à comprendre.

## Quand la choisir

- Quand de **nombreuses équipes** doivent travailler sans se gêner, ou quand certaines parties ont des besoins de
  puissance très différents.
- Quand l'équipe sait faire tourner et surveiller un système distribué.
- **Rarement comme premier choix** : on y arrive souvent à partir d'un monolithe modulaire, module par module.

## Dans notre banque

Quatre services séparés (comptes, virements, notifications, et un facteur pour les messages), plus la passerelle. Le
service des virements réessaie quand les comptes ne répondent pas, sans débiter deux fois. Si le facteur est en panne,
les messages attendent dans une boîte d'envoi ; les notifications arrivent dès qu'il revient.

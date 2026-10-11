# 9. CQRS (séparer l'écriture et la lecture)

🏠 [Sommaire](README.md) · ⬅️ [Les microservices](08-microservices.md) · ➡️ [L'Event Sourcing](10-event-sourcing.md) · 🔧 [Version technique](../09-cqrs.md) · 🌐 [English](../en/pour-tous/09-cqrs.md)

<p align="center"><img src="images/09.jpg" alt="Illustration : la cuisine d'un restaurant et l'ardoise des plats du jour" width="480"></p>

> **En une image** : un restaurant. En cuisine, on prépare les plats en suivant des règles strictes. En salle, une
> grande ardoise affiche les plats du jour et ce qui reste, mise à jour par un serveur dès qu'il en a le temps. Les
> clients lisent l'ardoise ; ils ne vont pas compter les assiettes en cuisine.

---

## L'idée

CQRS est un acronyme anglais qui veut dire, en gros : **une organisation pour écrire, une autre pour lire**.

Écrire et lire n'ont pas les mêmes besoins :

- **écrire** (faire un virement, ouvrir un compte), c'est appliquer des règles et ne jamais se tromper ;
- **lire** (afficher un relevé, des statistiques), c'est obtenir vite des informations toutes prêtes, sous des formes
  très variées.

On sépare donc les deux. Côté écriture, on applique les règles. Chaque fois que quelque chose change, on l'annonce. Côté
lecture, on écoute ces annonces et on prépare à l'avance des « fiches » prêtes à afficher : le relevé de chaque compte,
les totaux, les statistiques.

## Pourquoi c'est bien

- **Les lectures sont très rapides** : tout est déjà prêt, il n'y a plus qu'à servir.
- **Chaque côté est simple** : les règles ne sont pas encombrées par les besoins d'affichage, et l'affichage ne risque
  pas de casser les règles.
- **On peut ajouter un nouvel affichage** sans toucher aux règles.
- Idéal quand on lit beaucoup plus qu'on n'écrit (le cas de la plupart des applications).

## Ce que ça coûte

- **L'ardoise peut avoir un temps de retard.** Juste après un retrait, le relevé peut encore montrer l'ancien solde,
  le temps que la mise à jour passe. Il faut que ce soit acceptable pour les utilisateurs.
- **Des informations en double**, à maintenir à jour.
- Plus de pièces à comprendre qu'une application classique.

## Quand la choisir

- Quand les lectures sont très nombreuses, ou très différentes de ce qu'on écrit (tableaux de bord, relevés, recherche).
- Souvent pour **une partie** seulement de l'application, celle où les lectures posent problème.

## Dans notre banque

Le côté écriture vérifie les virements et annonce « argent retiré », « virement effectué ». Le côté lecture tient des
relevés tout prêts, avec le solde après chaque opération. Un retard de deux secondes est réglé exprès : on voit, juste
après un retrait, le relevé encore à l'ancien solde, puis rattraper son retard.

# 19. L'architecture spatiale (« space-based »)

🏠 [Sommaire](README.md) · ⬅️ [Les acteurs](18-modele-acteurs.md) · ➡️ [Le pipeline](20-pipeline.md) · 🔧 [Version technique](../19-space-based.md)

<p align="center"><img src="images/19.jpg" alt="Illustration : un supermarché un samedi après-midi" width="480"></p>

> **En une image** : un supermarché un samedi après-midi. Quand la foule arrive, on ouvre des caisses ; quand elle
> repart, on en ferme. Chaque caissière a sous la main tout ce qu'il faut pour encaisser, sans téléphoner au bureau à
> chaque article ; le bureau fait ses comptes plus tard, au calme.

---

## L'idée

Dans presque toutes les applications, chaque demande finit par passer par **la base de données**. Quand la foule arrive
(soldes, ouverture d'une billetterie, résultats d'un concours), c'est elle qui sature, et tout ralentit.

L'architecture spatiale sort la base du chemin :

- chaque **unité de traitement** (une caisse) garde **toutes les données en mémoire**, une copie complète ;
- les unités se tiennent au courant entre elles de chaque changement ;
- la base de données est mise à jour **en arrière-plan**, un peu plus tard ;
- quand la charge monte, on **ajoute des unités**, qui démarrent en recopiant la mémoire d'une autre ; quand elle
  baisse, on les retire.

Le nom vient de l'idée d'un « espace » de données partagé, dans lequel toutes les unités puisent.

## Pourquoi c'est bien

- **Très rapide** : tout est en mémoire.
- **Ça tient les pics** : on ajoute des unités presque instantanément.
- **La base peut même tomber un moment** sans que les utilisateurs s'en aperçoivent.

## Ce que ça coûte

- **Les collisions** : si deux unités modifient le même compte au même instant, l'une peut écraser l'autre. Il faut
  envoyer toujours le même client vers la même unité pour l'éviter, et même alors, une fenêtre de risque subsiste
  quand on ajoute ou retire des unités.
- **Tout doit tenir en mémoire**, ce qui coûte cher pour de gros volumes.
- **La base est en retard** sur la réalité.
- Une infrastructure délicate à faire tourner.

## Quand la choisir

- Pour des **pics de charge énormes et imprévisibles** que la base ne suit pas : billetteries, enchères, ventes flash.
- Quand quelques collisions rares sont acceptables, ou évitables par un bon aiguillage.

## Dans notre banque

On coupe la base de données : la banque continue de répondre, et les écritures attendent leur tour, puis partent quand
la base revient. Six cents ouvertures de compte simultanées font passer de deux à six unités, puis on redescend à deux
au calme. Et en aiguillant les clients n'importe comment, on voit le prix de la vitesse : des retraits acceptés qui se
perdent.

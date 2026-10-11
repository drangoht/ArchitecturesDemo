# 0. Les couches

**Nom anglais** : *Layered Architecture (N-tier)*

🏠 [Sommaire](README.md) · ➡️ [L'hexagonale](01-hexagonale.md) · 🔧 [Version technique](../00-couches.md) · 🌐 [English](../en/pour-tous/00-couches.md)

<p align="center"><img src="images/00.jpg" alt="Illustration : un restaurant sur trois étages" width="480"></p>

> **En une image** : un restaurant sur trois étages. La salle accueille les clients, la cuisine prépare les plats, la
> réserve garde les ingrédients. Chacun ne parle qu'à l'étage du dessous.

---

## L'idée

C'est la façon la plus classique, et la plus ancienne, d'organiser une application. On la range en **trois étages** :

- **en haut, l'accueil** : ce qui reçoit les demandes des utilisateurs (une page web, une application) ;
- **au milieu, les règles** : ce qui décide (« Alice a-t-elle assez d'argent pour virer 250 € ? ») ;
- **en bas, les données** : ce qui range et retrouve les informations dans la base de données.

Une demande descend les étages, la réponse remonte. L'accueil ne fouille jamais lui-même dans la réserve : il passe par la
cuisine.

## Pourquoi c'est bien

- **Tout le monde comprend tout de suite.** C'est la première chose qu'on apprend.
- **Chaque étage a son métier** : on sait où chercher quand quelque chose ne va pas.
- **Parfait pour démarrer** et pour les applications simples, qui consistent surtout à enregistrer et afficher des
  informations.

## Ce que ça coûte

Le défaut est caché dans le sens des flèches : **les règles dépendent de la réserve**. La cuisine est organisée autour de
la façon dont la réserve range ses ingrédients. Si l'on change de base de données, ou simplement sa façon de ranger, il
faut retoucher les règles.

Et pour vérifier une règle, il faut souvent faire tourner l'étage du dessous avec : difficile de tester la cuisine sans
réserve.

Presque toutes les autres architectures sont nées pour corriger ce défaut.

## Quand la choisir

- Pour une application petite ou moyenne, surtout faite de formulaires et de listes.
- Quand l'équipe débute, ou que le projet doit avancer vite.
- Comme point de départ, quitte à évoluer quand les règles deviennent riches.

## Dans notre banque

Trois blocs : l'accueil reçoit « Alice vire 250 € à Bob », les règles vérifient le solde, l'étage des données lit et
enregistre les comptes. Un garde-fou empêche l'accueil de sauter un étage pour aller directement aux données.

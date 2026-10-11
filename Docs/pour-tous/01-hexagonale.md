# 1. L'architecture hexagonale (« ports et adaptateurs »)

**Nom anglais** : *Hexagonal Architecture (Ports & Adapters)*

🏠 [Sommaire](README.md) · ⬅️ [Les couches](00-couches.md) · ➡️ [L'Onion](02-onion.md) · 🔧 [Version technique](../01-hexagonale.md) · 🌐 [English](../en/pour-tous/01-hexagonale.md)

<p align="center"><img src="images/01.jpg" alt="Illustration : un ordinateur portable" width="480"></p>

> **En une image** : un ordinateur portable. Ce qui compte est à l'intérieur ; autour, des **prises** standard. On y
> branche un écran, une souris ou une clé USB, et on peut les changer sans ouvrir l'ordinateur.

---

## L'idée

On met **les règles au centre**, et on leur interdit de connaître le monde extérieur. Elles ne savent pas s'il y a une
base de données, un site web ou une application mobile.

À la place, elles définissent des **prises** (les « ports ») : « j'ai besoin de quelqu'un qui sache retrouver un compte »,
« j'ai besoin de quelqu'un qui sache prévenir le client ». Autour, on branche des **adaptateurs** : un pour la vraie base
de données, un pour l'envoi d'e-mails, un pour le site web.

C'est l'inverse des couches : ce n'est plus la cuisine qui s'adapte à la réserve, c'est la réserve qui s'adapte aux
besoins de la cuisine.

## Pourquoi c'est bien

- **Les règles sont protégées.** Changer de base de données ou d'écran ne les touche pas.
- **Les tests deviennent faciles** : on branche de fausses prises (une base en mémoire, un faux envoi d'e-mails) et on
  vérifie les règles toutes seules, en une fraction de seconde.
- **Plusieurs entrées pour le même cœur** : un site web, une application en ligne de commande, un traitement de nuit
  utilisent exactement les mêmes règles.

## Ce que ça coûte

- **Plus de pièces** : chaque prise et chaque adaptateur est un élément de plus à écrire et à comprendre.
- **De la discipline** : rien n'empêche physiquement un développeur pressé de court-circuiter une prise. Il faut des
  vérifications automatiques pour le rappeler.

## Quand la choisir

- Quand les règles sont riches et doivent durer plus longtemps que les outils autour.
- Quand on veut tester beaucoup, et vite.
- Quand l'application doit parler à plusieurs mondes extérieurs (web, fichiers, autres systèmes).

## Dans notre banque

Le cœur décide du virement sans savoir où sont rangés les comptes. Le même cœur est branché à un site web, à une
console et à une fausse banque pour les tests. On a changé de « réserve » sans toucher une seule règle.

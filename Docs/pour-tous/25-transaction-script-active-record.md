# 25. Transaction Script et Active Record (le point de départ)

🏠 [Sommaire](README.md) · ⬅️ [MVC et MVVM](24-mvc-mvvm.md) · 🔧 [Version technique](../25-transaction-script-active-record.md) · 🌐 [English](../en/pour-tous/25-transaction-script-active-record.md)

<p align="center"><img src="images/25.jpg" alt="Illustration : une petite épicerie de village, un commerçant seul derrière son comptoir en bois" width="480"></p>

> **En une image** : la **petite épicerie** tenue par une seule personne. Pour chaque demande, l'épicier suit une
> **liste à cocher**, du début à la fin. Ou bien chaque article a sa **fiche cartonnée**, qui porte ses informations et
> ses règles, et qu'il remet lui-même dans le tiroir après l'avoir mise à jour.

---

## L'idée

Toutes les autres pages de cette série rangent une application d'une façon réfléchie. Celle-ci montre ce qu'on fait
**avant** de réfléchir à l'organisation : on écrit les règles directement, au plus court. Il y a deux façons de le faire.

**Transaction Script** (le script d'une opération) : pour chaque chose que l'application sait faire (retirer de
l'argent, faire un virement), on écrit **une procédure**, de haut en bas, comme une liste à cocher : ouvrir le registre,
lire le compte, vérifier qu'il y a assez d'argent, écrire le nouveau solde, refermer le registre. Si quelque chose se
passe mal en chemin, on **remet tout comme avant** : rien n'est à moitié fait.

**Active Record** (l'enregistrement actif) : chaque compte devient une **fiche** qui sait tout d'elle-même : ce qu'elle
contient (le titulaire, le solde), ses règles (« on ne retire pas plus que ce qu'on a ») et la façon de se ranger dans le
tiroir. On sort la fiche, on lui demande de faire le retrait, on la remet en place.

## Pourquoi c'est bien

- **C'est le plus simple** : rien à apprendre, rien entre les règles et les données. On voit tout d'un coup d'œil.
- **C'est rapide à écrire** : de nombreux sites web sont faits ainsi, et beaucoup n'ont jamais eu besoin de plus.
- **Avec les fiches**, chaque règle est écrite **une seule fois**, sur la fiche du compte.

## Ce que ça coûte

- **Avec les listes à cocher**, chaque liste recopie les vérifications dont elle a besoin. Le jour où une règle change
  (on autorise un petit découvert), il faut retrouver toutes les listes qui la contiennent.
- **Avec les fiches**, il ne faut pas oublier de les **remettre dans le tiroir** : une fiche mise à jour mais pas
  rangée, c'est un changement perdu.
- Dans les deux cas, les règles sont **collées au registre** : impossible de les vérifier sans lui. Quand l'application
  grandit, ce collage devient gênant, et c'est là que les autres architectures entrent en jeu.

## Quand la choisir

- Pour une application **petite ou simple** : un outil interne, un site de gestion, un prototype.
- Pour **commencer**, tout simplement : on n'en sort qu'au premier vrai problème (des règles recopiées partout, des
  vérifications impossibles sans la base de données). Chacune des autres pages raconte une de ces sorties.

## Dans notre banque

La même banque est écrite des deux façons, côte à côte, sur **le même registre** : un retrait fait avec la liste à cocher
se retrouve sur la fiche, et inversement. Un virement vers un compte qui n'existe pas est refusé, et l'argent déjà
retiré du compte d'Alice y revient aussitôt : la procédure a tout remis comme avant.

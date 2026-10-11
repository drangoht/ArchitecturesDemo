# 2. L'architecture en oignon

🏠 [Sommaire](README.md) · ⬅️ [L'hexagonale](01-hexagonale.md) · ➡️ [La Clean Architecture](03-clean.md) · 🔧 [Version technique](../02-onion.md)

<p align="center"><img src="images/02.jpg" alt="Illustration : un oignon, ou une ville fortifiée" width="480"></p>

> **En une image** : un oignon, ou une ville fortifiée. Le trésor est au centre ; chaque enceinte le protège ; on peut
> entrer vers le centre, jamais l'inverse.

---

## L'idée

C'est une cousine de l'hexagonale, avec un rangement plus précis. On dessine des **cercles** :

- **au centre**, les notions de base de la banque : un compte, un montant, ce qu'est un solde ;
- **autour**, les règles qui font travailler ces notions ensemble : comment se fait un virement ;
- **puis** ce qui orchestre une demande complète ;
- **à l'extérieur**, tout ce qui est technique : la base de données, le site web.

La règle d'or : **on ne regarde que vers l'intérieur**. Le centre ne sait rien des cercles qui l'entourent.

## Pourquoi c'est bien

- Même avantage que l'hexagonale : les règles ne dépendent ni de la base ni de l'écran.
- **Un rangement clair** pour les grosses applications : chaque idée a son cercle, on sait où la mettre.
- Le découpage peut être imposé par l'organisation même des fichiers : un cercle intérieur ne peut tout simplement pas
  utiliser un cercle extérieur.

## Ce que ça coûte

- **Beaucoup de cercles**, donc beaucoup de morceaux, pour une petite application.
- Une demande simple traverse plusieurs couches avant d'aboutir : de la cérémonie pour peu de chose.

## Quand la choisir

- Pour une application aux règles nombreuses, qui va vivre et grossir longtemps.
- Quand l'équipe aime les rangements stricts, que l'outil fait respecter tout seul.

## Dans notre banque

Au centre, le compte et le montant ; autour, la règle du virement ; à l'extérieur, la base et le site. Un compte ne sait
pas comment il est enregistré, et il n'a pas besoin de le savoir.

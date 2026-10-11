# 5. Le « Vertical Slice » (ranger par tranches verticales)

🏠 [Sommaire](README.md) · ⬅️ [Le Functional Core](04-coeur-fonctionnel.md) · ➡️ [Le Package by Component](06-composants.md) · 🔧 [Version technique](../05-vertical-slice.md)

<p align="center"><img src="images/05.jpg" alt="Illustration : un livre de recettes" width="480"></p>

> **En une image** : un livre de recettes. Chaque fiche contient tout ce qu'il faut pour un plat : les ingrédients, les
> étapes, la cuisson. On ne range pas « toutes les découpes » dans un chapitre et « toutes les cuissons » dans un autre.

---

## L'idée

Les architectures précédentes rangent par **étage technique** : tout l'accueil ensemble, toutes les règles ensemble,
toutes les données ensemble. Pour comprendre « retirer de l'argent », il faut ouvrir un morceau de chaque étage.

Le Vertical Slice fait l'inverse : on range par **fonctionnalité**. « Ouvrir un compte », « consulter un compte »,
« retirer de l'argent », « faire un virement » : chacune est une tranche qui va de la demande jusqu'à la base de données,
au même endroit.

On ne met en commun que ce qui sert vraiment à plusieurs tranches.

## Pourquoi c'est bien

- **Pour comprendre une fonctionnalité, on ouvre un seul endroit.**
- **Ajouter une fonctionnalité, c'est ajouter une fiche**, sans toucher aux autres. Moins de risques de casser ailleurs.
- **Chaque tranche fait à sa façon** : une simple consultation reste simple, une opération délicate reçoit plus de soin.
  Pas de procédure lourde imposée à tout le monde.
- Très proche de la façon dont on parle d'un produit : par fonctionnalités, pas par couches techniques.

## Ce que ça coûte

- **Des répétitions** : deux fiches peuvent refaire la même chose. Il faut repérer à temps ce qui mérite d'être mis en
  commun.
- Les règles sont moins protégées de la technique : chaque tranche parle directement à la base de données.
- Sans discipline, chaque tranche invente son propre style.

## Quand la choisir

- Pour une application qui grossit par ajout de fonctionnalités, avec des règles modestes.
- Quand une équipe veut livrer vite et souvent, une fonctionnalité à la fois.
- À l'intérieur d'un module simple d'un plus grand système.

## Dans notre banque

Un fichier par action : ouvrir, consulter, retirer, virer. Pour comprendre le retrait, on lit un seul fichier. La seule
règle mise en commun est « le solde doit suffire », parce que le retrait et le virement en ont tous les deux besoin.

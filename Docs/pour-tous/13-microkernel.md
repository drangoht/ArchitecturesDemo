# 13. Le « Microkernel » (un micro-noyau et ses extensions)

🏠 [Sommaire](README.md) · ⬅️ [Le serverless](12-serverless.md) · ➡️ [La saga orchestrée](14-saga-orchestree.md) · 🔧 [Version technique](../13-microkernel.md)

<p align="center"><img src="images/13.jpg" alt="Illustration : une perceuse et sa mallette d'embouts" width="480"></p>

> **En une image** : une perceuse et sa mallette d'embouts. La perceuse fait une seule chose, tourner ; selon l'embout
> qu'on y fixe, elle perce, visse ou ponce. On ajoute des embouts sans racheter la perceuse.

---

## L'idée

On construit un **noyau minimal** qui fait l'essentiel (tenir des comptes, débiter, créditer) et qui prévoit des
**emplacements** où brancher des **extensions** (on dit aussi « plugins »). Tout ce qui varie d'un client à l'autre, ou
d'une année à l'autre, devient une extension : un contrôle supplémentaire, un calcul de frais, un format d'export.

C'est le principe de votre navigateur et de ses extensions, d'un logiciel de dessin et de ses filtres, d'une console de
jeux et de ses cartouches.

## Pourquoi c'est bien

- **Le noyau reste petit et stable** : on y touche rarement, donc on le casse rarement.
- **On ajoute, retire ou met à jour une extension** sans réinstaller l'application, parfois même sans l'arrêter.
- **Chaque client peut avoir ses extensions** : la même application, des comportements différents.
- **D'autres peuvent écrire des extensions** (partenaires, autres équipes) en suivant le mode d'emploi publié par le noyau.

## Ce que ça coûte

- **Le mode d'emploi des extensions est un engagement** : le changer casse toutes les extensions existantes.
- **Une extension mal écrite** tourne à l'intérieur de l'application et peut la perturber : il faut savoir l'écarter.
- Bien choisir les emplacements est difficile : trop peu, et on ne peut rien étendre ; trop, et le noyau devient
  compliqué.

## Quand la choisir

- Pour un produit vendu à plusieurs clients qui veulent chacun **leurs propres règles**.
- Quand on veut laisser **d'autres** enrichir l'application.
- Quand certaines règles changent souvent (réglementation, offres commerciales).

## Dans notre banque

Le noyau tient les comptes. Quatre extensions s'y branchent : un plafond la nuit, une liste noire de bénéficiaires, des
frais de virement, et des exports de relevés en deux formats. On peut désactiver une extension, ou la modifier pendant
que l'application tourne : elle est rechargée toute seule. Une extension qui plante est mise de côté, sans emporter le
reste.

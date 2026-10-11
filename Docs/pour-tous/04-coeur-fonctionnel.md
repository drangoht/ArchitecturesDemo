# 4. Le cœur fonctionnel et la coquille

🏠 [Sommaire](README.md) · ⬅️ [La Clean Architecture](03-clean.md) · ➡️ [Les tranches verticales](05-vertical-slice.md) · 🔧 [Version technique](../04-coeur-fonctionnel.md)

<p align="center"><img src="images/04.jpg" alt="Illustration : le juge et l'huissier" width="480"></p>

> **En une image** : le juge et l'huissier. Le juge décide, sur dossier, sans quitter son bureau. L'huissier va chercher
> les pièces avant, et exécute la décision après.

---

## L'idée

On sépare l'application en deux parties très différentes :

- **le cœur** (le juge) : uniquement des décisions. On lui donne tout ce qu'il faut savoir (« Alice a 5 000 €, elle a
  déjà viré 1 500 € aujourd'hui, elle veut encore virer 1 000 €, le plafond du jour est de 2 000 € »), il répond par une
  décision (« accepté, voici les nouveaux soldes » ou « refusé, plafond du jour dépassé »). Il ne lit rien, n'écrit
  rien, n'envoie rien : il réfléchit ;
- **la coquille** (l'huissier) : tout ce qui touche au monde réel. Aller chercher les comptes, appeler le juge, puis
  enregistrer la décision et prévenir les gens.

Le cœur a une propriété précieuse : **avec le même dossier, il rend toujours la même décision**. Pas de surprise liée à
l'heure, à la base de données ou au réseau.

## Pourquoi c'est bien

- **Les règles se testent comme une table de multiplication** : tel dossier, telle décision. Pas besoin de base de
  données, de faux objets ou de mise en scène.
- **Moins de bugs étranges** : une décision ne peut pas changer à cause d'un effet de bord caché.
- Idéal pour les règles complexes : plafonds, frais, calculs, conditions qui s'enchaînent.

## Ce que ça coûte

- Il faut **tout réunir avant de décider**. Si le juge a parfois besoin d'une pièce qu'on ne connaît qu'en cours de
  route, l'organisation devient plus acrobatique.
- Une façon de penser différente, qui demande un temps d'adaptation aux équipes habituées à « faire au fil de l'eau ».

## Quand la choisir

- Pour isoler des règles compliquées et critiques (calculs financiers, tarifs, éligibilité).
- Souvent pour une partie de l'application seulement, celle où se trouve le vrai casse-tête.

## Dans notre banque

Le cœur reçoit le compte, le montant et ce qu'Alice a déjà viré aujourd'hui. Il rend une décision. La coquille charge le
compte, appelle le cœur, puis enregistre. Les règles se vérifient par dizaines de cas, sans aucune base de données.

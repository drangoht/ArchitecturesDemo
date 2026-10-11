# 3. La « Clean Architecture » (architecture propre)

🏠 [Sommaire](README.md) · ⬅️ [L'oignon](02-onion.md) · ➡️ [Le cœur fonctionnel](04-coeur-fonctionnel.md) · 🔧 [Version technique](../03-clean.md)

<p align="center"><img src="images/03.jpg" alt="Illustration : une mairie bien organisée" width="480"></p>

> **En une image** : une mairie bien organisée. Un guichet par démarche (carte d'identité, permis de construire,
> mariage) ; chaque guichet a sa procédure écrite ; et la réponse vous est remise par un employé qui la met en forme
> pour vous.

---

## L'idée

Popularisée par Robert C. Martin (« Uncle Bob »), elle reprend les cercles de l'oignon et ajoute deux idées :

- **Une démarche, un guichet.** Chaque chose que l'application sait faire (consulter un compte, faire un virement) est
  un « cas d'utilisation » à part, avec son entrée, sa procédure et sa sortie. En lisant la liste des guichets, on sait
  ce que fait l'application.
- **Le résultat sort par un présentateur.** Le guichet ne choisit pas comment la réponse sera affichée (page web,
  message, fichier). Il donne le résultat brut ; un autre se charge de le présenter.

## Pourquoi c'est bien

- **On lit l'application comme un sommaire** : la liste de ses cas d'utilisation dit ce qu'elle fait.
- **Chaque démarche se teste seule**, en vérifiant ce qu'elle a remis au présentateur.
- **Les règles sont totalement indépendantes** de la technique, comme dans l'hexagonale et l'oignon.

## Ce que ça coûte

- **De la paperasse.** Pour une simple consultation, il faut un guichet, un formulaire d'entrée, un formulaire de sortie
  et un présentateur. C'est beaucoup d'éléments pour des besoins modestes.
- Le risque de suivre la méthode à la lettre, même là où elle n'apporte rien.

## Quand la choisir

- Pour une application aux démarches nombreuses et importantes, qui doit durer des années.
- Quand plusieurs équipes y travaillent et ont besoin de repères communs très clairs.

## Dans notre banque

Un guichet « Consulter un compte », un guichet « Faire un virement ». Le guichet du virement ne fabrique pas la page web :
il annonce « virement fait, nouveau solde 750 € » ou « refusé, solde insuffisant », et un présentateur en fait une
réponse pour le site. Il y a même deux présentateurs pour la consultation : l'un répond aux programmes, l'autre imprime
un relevé lisible ; le guichet, lui, n'a pas changé.

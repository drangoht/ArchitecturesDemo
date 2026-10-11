# 10. L'« Event Sourcing » (garder l'historique plutôt que le résultat)

🏠 [Sommaire](README.md) · ⬅️ [CQRS](09-cqrs.md) · ➡️ [Le Domain-Driven Design](11-ddd.md) · 🔧 [Version technique](../10-event-sourcing.md)

<p align="center"><img src="images/10.jpg" alt="Illustration : le carnet de comptes d'autrefois" width="480"></p>

> **En une image** : le carnet de comptes d'autrefois. On n'y écrit pas « il me reste 750 € » en gommant le chiffre
> précédent : on note chaque opération, ligne après ligne, à l'encre. Le solde, on le recalcule en faisant l'addition.

---

## L'idée

Presque toutes les applications retiennent **l'état actuel** : « Alice a 750 € ». Comment on en est arrivé là est
oublié, ou noté à part.

L'Event Sourcing fait l'inverse : on retient **la liste de tout ce qui s'est passé**, dans l'ordre : « compte ouvert »,
« 1 000 € déposés », « 250 € virés à Bob ». On ne modifie jamais une ligne, on n'en efface jamais aucune : on ajoute.
L'état actuel n'est plus qu'un **calcul** : on rejoue l'histoire, et on obtient le solde.

Une erreur ? On ne la gomme pas : on écrit une correction, comme en comptabilité.

## Pourquoi c'est bien

- **Un historique complet et fiable**, gratuitement : on sait qui a fait quoi, et quand. Précieux pour une banque, la
  comptabilité, la logistique ou tout ce qui doit être audité.
- **Le voyage dans le temps** : on peut recalculer le solde tel qu'il était le 3 mars à midi.
- **De nouvelles questions sur le passé** : une statistique inventée aujourd'hui (« les plus gros retraits ») peut être
  calculée sur tout l'historique, depuis le premier jour.

## Ce que ça coûte

- **Une autre façon de penser**, qui demande du temps aux équipes.
- **Rien ne s'efface**, ce qui pose problème avec le droit à l'oubli : il faut prévoir comment rendre illisibles les
  données personnelles d'un client qui part.
- **Les formats évoluent** : une opération notée il y a cinq ans doit encore se relire aujourd'hui.
- Pour afficher vite, il faut des fiches prêtes à lire à côté (souvent avec CQRS).

## Quand la choisir

- Quand **l'historique est lui-même une information précieuse** : banque, assurance, comptabilité, suivi de colis.
- Pour une partie de l'application, rarement pour tout.
- À éviter pour une application qui ne fait qu'enregistrer et afficher des fiches simples.

## Dans notre banque

Toute la banque tient dans un fichier lisible, une ligne par événement, jamais modifié. On peut supprimer tout le reste :
au redémarrage, les soldes et les relevés sont recalculés à partir de ces lignes. Et l'on peut demander l'état d'un
compte « tel qu'il était » à n'importe quel moment.

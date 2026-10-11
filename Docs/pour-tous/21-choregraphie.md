# 21. La chorégraphie (tout par événements, sans chef)

🏠 [Sommaire](README.md) · ⬅️ [Le pipeline](20-pipeline.md) · ➡️ [Le figuier étrangleur](22-strangler-fig.md) · 🔧 [Version technique](../21-choregraphie.md)

<p align="center"><img src="images/21.jpg" alt="Illustration : une troupe de danseurs sans chef d'orchestre" width="480"></p>

> **En une image** : une troupe de danseurs sans chef d'orchestre. Chacun connaît son rôle et réagit aux mouvements des
> autres : quand l'un lève le bras, l'autre s'élance. Personne ne dirige, et pourtant la danse a lieu.

---

## L'idée

C'est le pendant de la [saga orchestrée](14-saga-orchestree.md), pour le même virement en plusieurs étapes, mais
**sans orchestrateur**. Chaque service écoute les annonces des autres et réagit en publiant les siennes :

- le service des comptes annonce « argent réservé » ;
- la conformité, qui l'entend, vérifie et annonce « virement autorisé » ;
- le change, qui l'entend, convertit et annonce « montant converti » ;
- et ainsi de suite jusqu'au bout.

Personne ne connaît le processus en entier : il **émerge** des réactions de chacun. Pour ajouter un participant (un
programme de fidélité qui offre des points), il suffit qu'il écoute la bonne annonce, sans rien changer aux autres.

## Pourquoi c'est bien

- **Les services sont très indépendants** : aucun ne connaît les autres, seulement les annonces.
- **Ajouter une réaction est trivial**, sans toucher à l'existant.
- **Pas de chef** dont tout dépend.

## Ce que ça coûte

- **Le processus n'est écrit nulle part.** Pour savoir « où en est ce virement ? », il faut reconstituer l'histoire à
  partir des annonces.
- **Une panne, c'est du silence** : si un service ne réagit pas, rien ne se passe, et personne ne s'en plaint. Il faut
  des délais et une surveillance pour le remarquer.
- **Les retours en arrière sont dispersés** : chaque service doit savoir lui-même quoi défaire, et quand.

## Quand la choisir

- Pour des **réactions** : prévenir, mettre à jour, calculer des points, archiver. Là, c'est idéal.
- Pour un processus important avec des règles d'échec précises, on préfère généralement l'orchestration.
- La règle souvent citée : **orchestrer les processus, chorégraphier les réactions**.

## Dans notre banque

Le même virement que la saga, avec les mêmes règles. Un historique montre chaque annonce, qui l'a publiée, et en réponse
à quoi. On met le change en panne : le virement reste en plan, sans erreur visible, jusqu'à ce qu'au bout de trente
secondes le service des comptes libère de lui-même l'argent réservé. Le service de fidélité a été ajouté après coup,
sans qu'aucun autre ne change.

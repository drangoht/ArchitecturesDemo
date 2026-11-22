# 26. Mediator (le standard téléphonique)

🏠 [Sommaire](README.md) · ⬅️ [Transaction Script et Active Record](25-transaction-script-active-record.md) · ➡️ [La résilience](27-resilience.md) · 🔧 [Version technique](../26-mediator.md) · 🌐 [English](../en/pour-tous/26-mediator.md)

<p align="center"><img src="images/26.jpg" alt="Illustration : une standardiste avec son casque, à l'accueil d'une entreprise" width="480"></p>

> **En une image** : le **standard téléphonique** d'une entreprise. On n'appelle pas directement la personne qui va
> s'occuper de nous : on appelle le standard, qui sait qui fait quoi, note chaque appel dans son registre, vérifie
> qu'on a bien son numéro de dossier, puis passe la communication.

---

## L'idée

Dans une application, chaque action (retirer de l'argent, faire un virement) est traitée par un morceau de code
précis. D'habitude, celui qui demande l'action appelle directement ce morceau de code, comme on compose le numéro
direct d'un collègue.

Avec un **médiateur**, on passe toujours par **le standard**. On lui envoie une demande (« retrait de 100 € sur le
compte d'Alice ») ; il sait à qui la confier, et il fait au passage, **pour toutes les demandes**, les mêmes petites
choses : noter l'appel dans le registre, vérifier que la demande est complète, et, quand c'est fini, ranger le dossier.

Le standard sait aussi faire des **annonces** : « un virement vient d'être fait ». Tous ceux que ça intéresse
l'entendent et réagissent (prévenir le destinataire, signaler un gros montant au service de contrôle), sans que
celui qui annonce sache qui écoute.

## Pourquoi c'est bien

- Les choses à faire **pour chaque demande** (noter, vérifier, ranger) sont écrites **une seule fois**, au standard,
  au lieu d'être répétées partout.
- Chacun ne fait plus que son vrai travail : celui qui traite un retrait ne s'occupe que du retrait.
- On ajoute une réaction à une annonce sans déranger celui qui l'a faite.

## Ce que ça coûte

- **On ne voit plus qui répond** : en lisant la demande, rien n'indique quel bureau la traite ; il faut chercher.
- Si **personne** n'est chargé d'un type de demande, on ne s'en aperçoit qu'au moment de l'appel.
- Les annonces sont faites **pendant** l'opération : si un seul auditeur a un problème, toute l'opération est annulée.
- On le confond souvent avec une autre idée, le **CQRS** (deux façons de ranger les données, une pour écrire et une
  pour lire). Le standard, lui, ne range rien : il passe les appels.

## Quand la choisir

- Quand beaucoup d'opérations différentes ont besoin des **mêmes vérifications** et du **même suivi**.
- Pas pour une petite application : un appel direct est plus simple à suivre.

## Dans notre banque

Toutes les opérations passent par le standard. Un retrait de 0 € est refusé dès la vérification, sans déranger
personne ; un retrait trop gros va jusqu'au bureau des comptes, qui le refuse. Le registre montre les deux. Un virement
de 1 000 € déclenche une annonce : Bob est prévenu, et le service de contrôle reçoit une alerte.

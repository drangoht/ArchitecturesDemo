# 14. La saga orchestrée

**Nom anglais** : *Orchestrated Saga*

🏠 [Sommaire](README.md) · ⬅️ [Le Microkernel](13-microkernel.md) · ➡️ [Le multi-tenant](15-multi-tenant.md) · 🔧 [Version technique](../14-saga-orchestree.md)

<p align="center"><img src="images/14.jpg" alt="Illustration : l'organisatrice d'un mariage" width="480"></p>

> **En une image** : l'organisatrice d'un mariage. Elle appelle chaque prestataire dans l'ordre (salle, traiteur,
> fleuriste, photographe), note où elle en est dans son carnet, relance ceux qui ne répondent pas. Et si le mariage est
> annulé, elle rappelle chacun pour décommander ce qui avait été réservé.

---

## L'idée

Certaines opérations demandent la participation de plusieurs services, en plusieurs étapes, parfois longues. Un virement
international, par exemple : réserver l'argent, vérifier la conformité, faire le change, créditer, débiter
définitivement, prévenir le client.

Une **saga** est cette suite d'étapes. Elle est **orchestrée** quand un chef d'orchestre, l'**orchestrateur**, la
conduit :

- il appelle chaque participant à son tour ;
- il **note chaque étape** dans un carnet, pour pouvoir reprendre exactement où il en était après une panne ;
- il **réessaie** quand un participant ne répond pas ;
- il sait **attendre** une décision humaine (un contrôle pour un gros montant) ;
- et si l'on ne peut pas aller au bout, il **défait** ce qui a été fait, étape par étape, dans l'ordre inverse : on
  appelle ça la **compensation**.

## Pourquoi c'est bien

- **Le processus est écrit à un seul endroit** : on peut le lire, le montrer, le faire valider par le métier.
- **On sait toujours où en est chaque opération**, et pourquoi elle s'est arrêtée.
- **Les pannes sont gérées** : réessais, délais, reprise, compensation.

## Ce que ça coûte

- **L'orchestrateur est une pièce de plus**, importante, à faire tourner et à surveiller.
- **Chaque participant doit accepter d'être appelé deux fois** sans faire les choses deux fois.
- **Certaines étapes ne se défont pas** : un e-mail envoyé reste envoyé. Passé un certain point, on ne recule plus, et
  il faut parfois une intervention humaine.

## Quand la choisir

- Pour un processus **important, en plusieurs étapes**, entre plusieurs services, avec des règles d'échec précises.
- Quand on doit pouvoir répondre à « où en est ma demande ? ».

## Dans notre banque

Un virement en six étapes. On peut provoquer des pannes : une panne passagère du change est absorbée par deux nouveaux
essais ; une panne durable déclenche la compensation, et l'argent réservé est libéré. Au-delà de 10 000 €, le virement
attend la décision d'un analyste, et il est annulé proprement si personne ne répond à temps. Arrêter l'orchestrateur en
plein virement, puis le relancer : il reprend exactement là où il en était.

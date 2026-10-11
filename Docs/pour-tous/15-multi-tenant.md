# 15. Le « multi-tenant » (multi-locataire : plusieurs clients, une seule application)

🏠 [Sommaire](README.md) · ⬅️ [La saga orchestrée](14-saga-orchestree.md) · ➡️ [Le Backend for Frontend](16-bff.md) · 🔧 [Version technique](../15-multi-tenant.md)

<p align="center"><img src="images/15.jpg" alt="Illustration : un immeuble d'appartements" width="480"></p>

> **En une image** : un immeuble d'appartements. Un seul bâtiment, une seule chaudière, un seul gardien ; mais chaque
> famille a son appartement, fermé à clé. Personne n'entre chez le voisin, et chacun aménage comme il l'entend.

---

## L'idée

Beaucoup de logiciels en ligne servent **plusieurs entreprises clientes** avec une **seule installation** : votre
messagerie professionnelle, votre outil de comptabilité, votre logiciel de réservation. Chaque client est un
**locataire** de l'application.

Le défi : tout partager (l'application, les serveurs, parfois la base de données) **sans jamais mélanger** les clients.
Le client A ne doit jamais voir, ni modifier, les données du client B. Chaque client peut aussi avoir ses propres
réglages : ses plafonds, ses tarifs, ses quotas.

Les plus gros clients, ou ceux qui le paient, peuvent avoir leur propre base de données : un pavillon à part dans la
même résidence.

## Pourquoi c'est bien

- **Une seule application à faire tourner et à mettre à jour** pour tous les clients : beaucoup moins cher.
- **Un nouveau client s'installe en quelques secondes**, sans rien déployer.
- **Chacun garde ses règles** grâce aux réglages par client.

## Ce que ça coûte

- **La moindre erreur peut faire fuiter des données** d'un client chez un autre : c'est la faute impardonnable. Il faut
  des garde-fous automatiques, pas seulement de la vigilance.
- **Le voisin bruyant** : un client qui consomme énormément peut ralentir tous les autres, sauf si on limite chacun.
- **Les mises à jour concernent tout le monde en même temps.**

## Quand la choisir

- Pour un logiciel vendu **en ligne, sur abonnement**, à de nombreuses organisations.
- Quand le coût de faire tourner une installation par client deviendrait trop élevé.

## Dans notre banque

L'application sert trois banques. La même adresse renvoie le compte d'Alice à 1 000 €, 3 000 € ou 25 000 € selon la
banque qui la demande, sans que les règles de la banque aient à s'en soucier : le tri est fait automatiquement, et une
demande qui ne dit pas de quelle banque elle vient est refusée plutôt que de tout montrer. Deux banques partagent une
base, la troisième a la sienne. Un client trop gourmand est freiné, sans ralentir les autres.

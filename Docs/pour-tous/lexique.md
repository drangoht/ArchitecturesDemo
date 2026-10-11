# Petit lexique

🏠 [Sommaire](README.md) · 🌐 [English](../en/pour-tous/lexique.md)

Les mots qui reviennent dans ces pages, expliqués simplement.

---

## Les mots de base

**Application** : un programme qui rend un service : consulter ses comptes, réserver un billet, envoyer un message.

**Règles métier** (ou simplement « le métier ») : ce que l'application doit savoir de son domaine, indépendamment de
l'informatique. Pour une banque : « on ne vire pas plus que ce qu'on a », « un virement débite un compte et en crédite
un autre ». C'est la partie la plus précieuse : c'est elle qu'on veut protéger.

**Données** : ce que l'application doit retenir : les comptes, les soldes, l'historique.

**Base de données** : l'endroit où les données sont rangées pour ne pas être perdues quand on éteint l'ordinateur.
Une sorte d'armoire à fiches très rapide.

**Serveur** : un ordinateur, souvent dans un centre de données, qui fait tourner l'application pour les autres. Votre
téléphone lui pose des questions, il répond.

**Interface** (ou écran) : ce que voit et touche l'utilisateur : une page web, une application mobile, une fenêtre.

**Requête** : une question ou une demande envoyée à un serveur : « donne-moi le solde d'Alice », « fais ce virement ».

**API** : le guichet d'une application pour les autres programmes. Elle dit quelles demandes on peut faire et sous quelle
forme. Comme le menu d'un restaurant : on ne va pas en cuisine, on commande ce qui est proposé.

**Réseau** : ce qui relie les ordinateurs entre eux. Rapide, mais pas parfait : un message peut arriver en retard, en
double, ou se perdre.

---

## Les mots de l'organisation

**Module** : un morceau d'une application qui s'occupe d'un sujet (les comptes, les virements, les notifications), un
peu comme un service dans une entreprise.

**Service** (ou microservice) : un module devenu un programme à part entière, qui tourne seul et qu'on interroge par le
réseau. Comme un service d'entreprise qui aurait déménagé dans son propre bâtiment.

**Dépendance** : quand un morceau a besoin d'un autre pour fonctionner. Si la cuisine a besoin de la réserve, la cuisine
« dépend » de la réserve : réorganiser la réserve oblige à changer les habitudes de la cuisine.

**Déployer** : installer une nouvelle version de l'application pour que les utilisateurs s'en servent. Un « déploiement »,
c'est une mise en service.

**Événement** : une annonce de quelque chose qui vient de se passer : « un virement a été fait ». Ceux que ça intéresse
l'écoutent et réagissent ; celui qui l'annonce ne sait pas forcément qui écoute.

**Message** : une information envoyée d'un morceau du système à un autre, comme une lettre. On peut l'envoyer sans
attendre la réponse.

**Test** : un petit programme qui vérifie automatiquement que l'application fait bien ce qu'on attend. On les relance
après chaque modification, comme une check-list avant le décollage d'un avion.

---

## Les idées qui reviennent partout

**Idempotence** : pouvoir répéter une demande sans risque. Appuyer cinq fois sur le bouton d'appel de l'ascenseur ne fait
pas venir cinq ascenseurs. Indispensable dès qu'un message peut arriver en double : un virement envoyé deux fois ne doit
débiter qu'une fois.

**Cohérence à terme** : accepter qu'une information soit en retard, pendant un court moment, sur une autre. Le relevé
bancaire ne montre pas toujours le paiement fait il y a une minute, mais il finira par le montrer.

**Compensation** : défaire proprement ce qui a été fait quand la suite échoue. Si le voyage est annulé après la
réservation de l'hôtel, on ne « gomme » pas la réservation : on l'annule, et on se fait rembourser.

**Boîte d'envoi** (« outbox ») : noter dans le même carnet, au même moment, ce qu'on a fait ET le message qu'on doit
envoyer pour le dire. Si le facteur ne passe pas, le message attend dans la boîte : il ne sera pas oublié.

**Concurrence** : plusieurs personnes qui modifient la même chose au même moment. Deux retraits simultanés sur le même
compte ne doivent pas faire disparaître l'un des deux. Il faut un arbitre.

**Rayon d'impact** : jusqu'où une panne fait des dégâts. Une ampoule qui grille dans une pièce, ou tout le quartier
plongé dans le noir.

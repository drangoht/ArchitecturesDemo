# 12. Le « serverless » (sans serveur à gérer)

🏠 [Sommaire](README.md) · ⬅️ [Le Domain-Driven Design](11-ddd.md) · ➡️ [Le Microkernel](13-microkernel.md) · 🔧 [Version technique](../12-serverless.md) · 🌐 [English](../en/pour-tous/12-serverless.md)

<p align="center"><img src="images/12.jpg" alt="Illustration : le taxi plutôt que la voiture" width="480"></p>

> **En une image** : le taxi plutôt que la voiture. Pas d'achat, pas d'entretien, pas de parking : on appelle quand on
> en a besoin, on paie la course. Mais il faut parfois attendre qu'il arrive, et à force de longs trajets tous les jours,
> la voiture aurait coûté moins cher.

---

## L'idée

« Serverless » veut dire « sans serveur »… à gérer soi-même. Il y a bien des serveurs, mais ils appartiennent à un
fournisseur (Amazon, Microsoft, Google), qui s'occupe de tout.

On n'écrit plus une application qui tourne jour et nuit, mais des **petites fonctions**, chacune réveillée par un
événement : une demande sur le site, un fichier déposé, un message reçu, une heure qui sonne. Le fournisseur lance la
fonction, la multiplie s'il y a beaucoup de demandes, l'éteint quand il n'y a plus rien à faire, et **facture à
l'usage**.

## Pourquoi c'est bien

- **Rien à installer ni à entretenir** : pas de machine à surveiller, à mettre à jour, à redémarrer.
- **Ça grandit tout seul** : mille demandes d'un coup, et le fournisseur lance mille copies de la fonction.
- **On ne paie que ce qu'on utilise** : une application peu utilisée coûte presque rien.

## Ce que ça coûte

- **Le démarrage à froid** : une fonction qui dormait met un moment à se réveiller ; le premier utilisateur attend.
- **Aucune mémoire entre deux appels** : tout ce qu'il faut retenir doit être rangé chez le fournisseur.
- **Tout peut arriver deux fois** : un message peut être livré en double, il faut s'en protéger.
- **On dépend du fournisseur**, de ses outils et de ses prix ; changer de fournisseur demande du travail.
- Pour une activité **forte et constante**, la facture peut dépasser celle d'un serveur classique.

## Quand la choisir

- Pour des traitements **irréguliers** : des pics de temps en temps, du calme le reste du temps.
- Pour des réactions à des événements : traiter un fichier dès qu'il arrive, envoyer un message, faire un calcul la nuit.
- Pour démarrer vite, sans équipe pour gérer des serveurs.

## Dans notre banque

Le fournisseur est simulé, mais il se comporte comme un vrai : la première demande attend que la fonction se réveille,
les suivantes sont rapides. Déposer un fichier de huit virements réveille huit copies de la fonction qui exécute un
virement, en même temps. Et une facture, au tarif réel, montre qu'une démonstration complète coûte quelques millièmes
de centime.

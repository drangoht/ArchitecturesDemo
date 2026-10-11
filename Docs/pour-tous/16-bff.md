# 16. Le « Backend for Frontend » (un assistant par application)

🏠 [Sommaire](README.md) · ⬅️ [Le multi-locataire](15-multi-tenant.md) · ➡️ [Les cellules](17-cell-based.md) · 🔧 [Version technique](../16-bff.md)

<p align="center"><img src="images/16.jpg" alt="Illustration : un grand hôtel avec un majordome par client" width="480"></p>

> **En une image** : un grand hôtel avec un majordome par client. Celui du client pressé va tout chercher en même temps
> et lui apporte l'essentiel sur un petit plateau ; celui du client qui a le temps lui présente tout, en détail, sur une
> grande table. Les cuisines, elles, sont les mêmes pour tout le monde.

---

## L'idée

Une banque a souvent **plusieurs applications** pour ses clients : un site web, une application mobile. Elles ont des
besoins opposés : grand écran et bonne connexion d'un côté, petit écran et réseau parfois lent de l'autre.

Plutôt que d'obliger les services de la banque à satisfaire tout le monde, on donne à chaque application **son propre
assistant** sur le serveur, son « backend » (d'où le nom : un backend pour chaque « frontend », chaque application). Cet
assistant :

- va chercher **en une fois**, auprès des différents services, tout ce dont un écran a besoin ;
- **taille la réponse** pour cet écran : rien de superflu pour le mobile ;
- **garde les secrets** de connexion côté serveur, hors de portée du navigateur ;
- **s'adapte** si un service ne répond pas, en affichant l'écran sans cette partie.

## Pourquoi c'est bien

- **Des applications plus rapides** : un seul aller-retour par écran au lieu d'une dizaine.
- **Chaque équipe d'application avance seule** : elle fait évoluer son assistant sans négocier avec les autres.
- **Plus de sécurité** pour le site web, puisque les secrets ne vont jamais dans le navigateur.

## Ce que ça coûte

- **Une pièce de plus par application**, à écrire et à faire tourner.
- **Des répétitions** entre les assistants.
- **La tentation d'y mettre des règles de la banque**, qui devraient rester dans les services.

## Quand la choisir

- Quand **plusieurs applications** très différentes utilisent les mêmes services.
- Quand les écrans ont besoin de données venant de nombreux services à la fois.

## Dans notre banque

Le tableau de bord du site demande une seule chose à son assistant, qui interroge sept services en même temps. La réponse
pour le mobile est plus de cinq fois plus légère. Si le service des offres est en panne, le tableau de bord s'affiche
quand même, sans les offres. Et l'assistant ne donne aucun passe-droit : Alice ne peut pas toucher au compte de Bob.

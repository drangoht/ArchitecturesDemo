# 24. MVC et MVVM (organiser l'écran)

🏠 [Sommaire](README.md) · ⬅️ [REST, gRPC, GraphQL](23-rest-grpc-graphql.md) · 🔧 [Version technique](../24-mvc-mvvm.md) · 🌐 [English](../en/pour-tous/24-mvc-mvvm.md)

<p align="center"><img src="images/24.jpg" alt="Illustration : le courrier qu'on envoie et qu'on reçoit, et le tableau de bord d'une voiture" width="480"></p>

> **En une image** : MVC, c'est **le courrier**. On remplit un formulaire, on l'envoie, et l'on reçoit en retour une
> nouvelle lettre, toute prête. MVVM, c'est **le tableau de bord d'une voiture** : les aiguilles bougent en direct, à
> chaque instant, sans qu'on ait rien à demander.

---

## L'idée

Les autres pages organisent ce qu'il y a derrière l'écran. Celle-ci organise **l'écran lui-même**. Dans les deux cas,
on sépare trois rôles pour ne pas tout mélanger :

- **le modèle** : les règles et les données de la banque, qui ne savent rien des écrans ;
- **la vue** : ce qu'on voit, et rien d'autre ;
- **un intermédiaire**, qui fait le lien. C'est là que les deux approches diffèrent.

**MVC** (Modèle, Vue, Contrôleur), sur le web : à chaque clic, le navigateur envoie une demande. Le **contrôleur** la
reçoit, consulte la banque, choisit la page à renvoyer et la remplit. La page est fabriquée, envoyée, puis oubliée. Au
clic suivant, on recommence.

**MVVM** (Modèle, Vue, Vue-Modèle), dans les applications de bureau et mobiles : l'écran est un objet vivant, le
**vue-modèle**, qui garde tout ce qu'il y a à l'écran (ce qui est tapé, sélectionné, affiché) et sait quoi faire quand
on clique. La vue est **branchée** dessus : chaque fois qu'une information change, l'écran se met à jour tout seul, comme
une aiguille du tableau de bord.

## Pourquoi c'est bien

- **MVC** : simple et robuste ; le serveur ne retient rien entre deux clics ; fonctionne partout, même sur un vieux
  téléphone.
- **MVVM** : l'écran **réagit pendant qu'on tape** (une erreur s'affiche avant même d'envoyer, le bouton s'active quand
  tout est bon) ; et tout le comportement de l'écran se vérifie **sans écran**, par des tests automatiques.

## Ce que ça coûte

- **MVC** : on ne découvre ses erreurs qu'après avoir envoyé le formulaire ; chaque étape (une confirmation) est une
  page de plus.
- **MVVM** : plus de mécanique ; et un écran qui vit longtemps demande de la rigueur (ne pas afficher une réponse arrivée
  en retard, ne pas valider deux fois sur un double clic).

## Quand la choisir

C'est surtout **la plateforme** qui décide :

- **MVC** pour un site web dont les pages sont fabriquées par le serveur ;
- **MVVM** pour une application de bureau ou mobile, qui reste ouverte et réagit en continu.

## Dans notre banque

Le même écran de virement, construit deux fois. En MVC, sur le site : on envoie le formulaire, on reçoit la réponse ; un
montant trop élevé passe d'abord par une page de confirmation, puis est refusé par la banque. En MVVM, dans une fenêtre :
le même montant est signalé dès qu'on le tape, et le bouton reste grisé. Une deuxième vue, toute simple, au clavier,
utilise exactement le même vue-modèle que la fenêtre.

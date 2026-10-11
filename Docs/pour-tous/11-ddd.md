# 11. Le « Domain-Driven Design » (concevoir à partir du métier)

🏠 [Sommaire](README.md) · ⬅️ [L'Event Sourcing](10-event-sourcing.md) · ➡️ [Le serverless](12-serverless.md) · 🔧 [Version technique](../11-ddd.md) · 🌐 [English](../en/pour-tous/11-ddd.md)

<p align="center"><img src="images/11.jpg" alt="Illustration : un architecte qui, avant de dessiner la maison, passe du temps avec la famille" width="480"></p>

> **En une image** : un architecte qui, avant de dessiner la maison, passe du temps avec la famille. Il apprend ses mots,
> ses habitudes, ce qui compte pour elle. Puis il dessine une maison où chaque pièce porte le nom que la famille lui donne.

---

## L'idée

Ce n'est pas une façon de ranger des dossiers, mais une **façon de concevoir**. Elle tient en quelques principes :

- **Parler la langue du métier.** Les développeurs et les experts de la banque se mettent d'accord sur un vocabulaire
  commun, et ce vocabulaire se retrouve tel quel dans l'application. Si les conseillers parlent de « plafond de retrait
  journalier », l'application aussi.
- **Des notions qui se protègent elles-mêmes.** Un IBAN invalide ne peut tout simplement pas exister dans l'application ;
  on ne peut pas additionner des euros et des dollars par erreur.
- **Le même mot ne veut pas dire la même chose partout.** Pour le service qui tient les comptes, un compte a un solde et
  des opérations. Pour le service qui surveille la fraude, c'est un dossier avec des alertes. Plutôt qu'un modèle
  énorme qui veut tout dire, on découpe en **contextes**, chacun avec son propre vocabulaire, et l'on organise leurs
  échanges.
- **Se protéger des vieux systèmes** : quand on doit parler à un logiciel ancien au format étrange, un traducteur
  empêche ses bizarreries d'entrer chez nous.

## Pourquoi c'est bien

- **Moins de malentendus** entre ceux qui connaissent le métier et ceux qui écrivent l'application.
- **Des règles plus sûres**, puisque les erreurs grossières deviennent impossibles.
- **Un bon découpage** : les contextes disent où tracer les frontières entre modules ou services.

## Ce que ça coûte

- **Il faut des experts du métier disponibles**, souvent et longtemps.
- **Beaucoup de soin** pour des besoins parfois simples : pour une application qui ne fait qu'enregistrer des fiches,
  c'est disproportionné.

## Quand la choisir

- Quand le métier est **riche et compliqué**, et que c'est lui qui fait la valeur de l'application.
- Pour **décider comment découper** un grand système, avant de choisir entre monolithe modulaire et microservices.

## Dans notre banque

Deux contextes : la tenue des comptes et la surveillance de la fraude, chacun avec son petit dictionnaire. Au troisième
virement en dix minutes, la surveillance soupçonne quelque chose et **demande** le blocage du compte : elle ne le bloque
pas elle-même, ce n'est pas son rôle. Et un vieux système de taux de change, au format bizarre, est traduit à l'entrée.

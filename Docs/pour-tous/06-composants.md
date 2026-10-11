# 6. Le « Package by Component » (ranger par composants)

🏠 [Sommaire](README.md) · ⬅️ [Le Vertical Slice](05-vertical-slice.md) · ➡️ [Le monolithe modulaire](07-monolithe-modulaire.md) · 🔧 [Version technique](../06-composants.md) · 🌐 [English](../en/pour-tous/06-composants.md)

<p align="center"><img src="images/06.jpg" alt="Illustration : les services d'une entreprise" width="480"></p>

> **En une image** : les services d'une entreprise. Chacun a un accueil où l'on dépose ses demandes ; on n'entre pas dans
> les bureaux pour fouiller dans les armoires. Et le badge n'ouvre que la porte de l'accueil.

---

## L'idée

On découpe l'application en grands **sujets** : ici, « les comptes » et « les virements ». Chaque sujet devient un
composant avec :

- **une seule porte d'entrée publique**, un accueil qui dit ce que le composant sait faire (« débiter un compte »,
  « donner un solde ») ;
- **tout le reste fermé à clé** : ses règles, ses données, son rangement interne.

Le service des virements a besoin des comptes ? Il passe par leur accueil, comme tout le monde. Et ce n'est pas une
simple consigne : l'outil qui fabrique l'application **refuse** tout accès à l'intérieur d'un autre composant.

## Pourquoi c'est bien

- **Les frontières sont garanties** par l'outil, pas seulement par la bonne volonté des développeurs.
- **Chaque composant peut changer son intérieur** sans prévenir les autres, tant que son accueil reste le même.
- **On peut confier un composant à une équipe**, qui sait exactement ce qu'elle doit offrir aux autres.

## Ce que ça coûte

- Il faut **bien penser les accueils** : ce qu'on y met, on devra le garder longtemps.
- Chaque composant ayant ses propres données, une opération qui touche deux composants (un virement) n'est plus
  automatiquement « tout ou rien ». Si la seconde moitié échoue, il faut défaire la première à la main.

## Quand la choisir

- Quand l'application a des sujets bien distincts et grossit.
- Quand on veut des frontières solides sans multiplier les programmes à installer.
- Comme première marche vers le monolithe modulaire, puis éventuellement les microservices.

## Dans notre banque

Deux composants, chacun avec sa base. Les virements ne lisent jamais les données des comptes : ils demandent à l'accueil
des comptes de débiter puis de créditer. Si le crédit échoue, ils rendent l'argent au compte de départ.

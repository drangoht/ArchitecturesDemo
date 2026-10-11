# 17. L'architecture cellulaire (« cell-based »)

**Nom anglais** : *Cell-Based Architecture*

🏠 [Sommaire](README.md) · ⬅️ [Le Backend for Frontend](16-bff.md) · ➡️ [Les acteurs](18-modele-acteurs.md) · 🔧 [Version technique](../17-cell-based.md)

<p align="center"><img src="images/17.jpg" alt="Illustration : des serres identiques et indépendantes, côte à côte" width="480"></p>

> **En une image** : une rangée de serres identiques, chacune avec ses plantes, son chauffage et son arrosage. Si la
> vitre de l'une se brise une nuit de gel, seules ses plantes souffrent ; les autres serres ne s'aperçoivent de rien.

---

## L'idée

D'habitude, quand une application grandit, on l'agrandit : plus de serveurs, une plus grosse base. Mais tout le monde
partage alors le même sort : une panne, ou une mise à jour ratée, touche **tous** les clients.

L'architecture cellulaire fait autrement : on fait tourner **plusieurs copies complètes** de l'application, les
**cellules**, et chacune ne sert **qu'une partie des clients**. Un petit aiguilleur, à l'entrée, sait dans quelle cellule
se trouve chaque client et l'y envoie.

Quand toutes les cellules sont pleines, on **ajoute une cellule** plutôt que d'en agrandir une. Et une nouvelle version
est installée d'abord dans **une seule** cellule, qu'on observe, avant de passer aux autres.

## Pourquoi c'est bien

- **Une panne ne touche qu'une partie des clients.** C'est le grand avantage : on limite les dégâts.
- **Les mises à jour sont prudentes** : une version défectueuse est arrêtée dans la première cellule.
- **Chaque cellule a une taille connue**, testée, maîtrisée : pas de mauvaise surprise en grandissant.

## Ce que ça coûte

- **Les échanges entre cellules sont compliqués** : un virement entre deux clients de cellules différentes devient une
  opération à distance, qui doit prévoir ses échecs.
- **Tout est multiplié** : N copies de l'application, N bases, N choses à surveiller.
- Il faut savoir **déménager un client** d'une cellule à l'autre.

## Quand la choisir

- À **très grande échelle**, quand une panne générale coûte plus cher que plusieurs copies de l'infrastructure.
- Quand les clients sont assez indépendants les uns des autres. C'est ainsi que fonctionnent certains services d'Amazon
  ou de Slack.

## Dans notre banque

Trois cellules, chacune avec quelques clients. On arrête l'une d'elles : ses clients reçoivent une erreur, les autres ne
s'aperçoivent de rien. On déploie une version contenant un bogue : elle est arrêtée dans la première cellule et annulée,
les deux autres ne la reçoivent jamais. Et un client peut déménager d'une cellule à l'autre, en n'étant indisponible
qu'un instant.

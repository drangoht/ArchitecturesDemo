# Fiche 4 · Functional Core, Imperative Shell

> **En une phrase** : des règles **pures** qui renvoient une décision ; une fine **coquille** autour fait toutes les entrées/sorties.

**Famille** : organisation du code · **Origine** : Gary Bernhardt, « Boundaries », 2012 ·
**Démo** : [`04-FunctionalCore`](../../04-FunctionalCore) (port 5104) · 📖 [Page complète](../04-coeur-fonctionnel.md) ·
🌐 [English](../en/fiches/04-coeur-fonctionnel.md)

```mermaid
flowchart LR
    L["① LIRE<br/>base, horloge, requête"] -->|valeurs immuables| D["② DÉCIDER (pur)<br/>Rules.Transfer(…)<br/>→ TransferDecision"]
    D -->|"Accepted / Refused"| A["③ APPLIQUER<br/>écrire, journaliser, répondre"]
```

## L'essentiel

- Le cœur : pas d'`async`, pas d'interface, pas de base, pas d'horloge. **Des valeurs en entrée, une décision en sortie.**
- La coquille : impérative, mince, **aucune décision**.
- Les décisions sont des **données** : journalisables, rejouables.
- Tests du cœur : valeurs → valeur, **sans aucun mock**, en millisecondes.

| ✅ Quand l'utiliser | ❌ Difficile si |
|---|---|
| Règles complexes : tarification, éligibilité, plafonds, calculs | La décision exige des lectures en cascade |
| À l'intérieur de n'importe quelle autre architecture | L'équipe n'a pas l'habitude du style fonctionnel |

**Piège** : il faut **tout charger avant** de décider.

**Voisins** : l'hexagonale poussée à l'extrême ; le même « décider / appliquer » est au cœur de
l'[Event Sourcing](10-event-sourcing.md).

---
⬅️ [Clean](03-clean.md) · [Index des fiches](README.md) · ➡️ [Vertical Slice](05-vertical-slice.md)

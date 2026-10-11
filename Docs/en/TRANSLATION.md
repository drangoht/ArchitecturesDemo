# English translation — plan, status and conventions

The documentation exists in **two versions kept side by side**: French (the original, in `Docs/`) and English (in
`Docs/en/`, same tree, **same file names**). The code of the demos stays in French for now; its translation will be
decided once the documentation is done.

## Status

| Batch | Content | Words (FR) | Status |
|---|---|---|---|
| 1 | Structure, this file, `Docs/en/README.md`, the 25 cheat sheets + index, English PDF, root `README.en.md` | ~23,000 | ✅ done (2026-10-11) |
| 2 | Full pages 0 to 8 (`Docs/0x-*.md`) | ~8,800 | ✅ done (2026-10-11) |
| 3 | Full pages 9 to 16 + `concepts-transverses.md` + `noms-architectures.md` | ~17,000 | ✅ done (2026-10-11) |
| 4 | Full pages 17 to 24 | ~19,000 | ✅ done (2026-10-11) |
| 5 | `pour-tous/` (25 pages, index, glossary `lexique.md`; images shared) | ~12,600 | ✅ done (2026-10-11) |
| 6 | `differences/` (17 comparisons + index) | ~14,700 | ✅ done (2026-10-11) |
| 7 | Code (identifiers, comments, routes, tests) — strategy to be decided | 28,000 lines | ⏸ later |

The whole documentation is now available in both languages (batches 1 to 6). From now on, **every change to a French
page goes with the same change to its English twin** (and vice versa), in the same commit; a new demo gets its pages in
both languages. Then run the tools below.

## Conventions

- **Mirror tree**: `Docs/x.md` ↔ `Docs/en/x.md`, `Docs/fiches/x.md` ↔ `Docs/en/fiches/x.md`, and so on. File names
  stay the French ones, so links between pages are identical in both languages.
- **Language switch**: each page has a `🌐 [English](…)` / `🌐 [Français](…)` link to its twin (removed from the PDFs).
- **Not yet translated**: an English page links to the French page and marks it *(French)* or *FR*. When a batch is
  done, replace those links by their English twin (e.g. in the cheat sheets, `../../00-couches.md` → `../00-couches.md`
  and the label "Full page (French)" → "Full page").
- **Code references stay in French**: folder, project, class, method, route and JSON field names (`ComptesService`,
  `/virements`, `Micro.Lanceur`) are quoted exactly as in the code. In Mermaid diagrams, identifiers stay as they are;
  descriptive labels are translated.
- **Code blocks are never translated** (the code itself is in French): every `csharp`, `xml`, `json`, `powershell`,
  `http`… block is a byte-for-byte copy of the French page's block, comments and messages included. When a French
  comment carries the point, the surrounding English prose paraphrases it. Only the annotations of project trees
  (blocks without a language) are translated; names stay as they are.

## Tools (`Docs/en/outils/`)

Run after each batch:

- `python Docs/en/outils/liens-lot.py`: links the English pages that now exist (cheat sheets' "Full page", index
  "Full page" column, links between pages), points the missing ones to the French page marked *(FR)*, and adds the
  `🌐 English` link to the French twins.
- `python Docs/en/outils/verif-code.py`: checks that every code block is identical to the French one (expected: 0 gaps).
- `python Docs/en/outils/verif-mermaid.py Docs/en/*.md`: renders every Mermaid diagram in headless Chrome and lists
  syntax errors (expected: 0 errors).
- `python Docs/fiches/pdf/generer-pdf.py --en`, then check the PDF still has 27 pages.
- **Architecture names**: English names follow [the names table](../noms-architectures.md). The 8 architectures with a
  French name show it under the title (`**French name**: *…*`), the mirror of the French pages' `**Nom anglais**` line.
- **Amounts**: `€250`, `€1,000` (English number format).
- **PDF**: `python Docs/fiches/pdf/generer-pdf.py --en` → `Docs/en/fiches/pdf/architecture-cheat-sheets.pdf`;
  one cheat sheet per page (27 pages): check the page count after any change.

## Glossary

| French | English |
|---|---|
| métier | business (logic), domain |
| compte, virement, retrait, dépôt, solde | account, transfer, withdrawal, deposit, balance |
| couche, cœur, coquille | layer, core, shell |
| cas d'utilisation, interacteur | use case, interactor |
| dépôt (de données), unité de travail | repository, unit of work |
| objet-valeur, agrégat, contexte délimité | value object, aggregate, bounded context |
| langage omniprésent, couche anti-corruption | ubiquitous language, anti-corruption layer |
| cohérence à terme, concurrence optimiste | eventual consistency, optimistic concurrency |
| idempotence, compensation, réessai, échéance | idempotency, compensation, retry, deadline |
| courtier, passerelle, file, lettres mortes | broker, gateway, queue, dead letters |
| locataire, voisin bruyant | tenant, noisy neighbor |
| rayon d'impact, plan de contrôle | blast radius, control plane |
| boîte aux lettres, superviseur | mailbox, supervisor |
| unité de traitement, grille en mémoire, pompe | processing unit, in-memory grid, pump |
| filtre, tuyau borné, contre-pression, goulot | filter, bounded pipe, back-pressure, bottleneck |
| façade, ancien / nouveau système, ombre | façade, legacy / new system, shadow |
| liaison, commande (MVVM) | binding, command |
| démarrage à froid, fournisseur | cold start, provider |
| fiche (mémo) | cheat sheet |
| déroulement | walkthrough |
| garde-fou | safeguard |
| test d'architecture | architecture test |

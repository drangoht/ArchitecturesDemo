"""Après un lot de traduction : relie les pages anglaises entre elles, renvoie au français celles qui manquent.

Pour chaque page anglaise (Docs/en/**, README.en.md) :
- un lien vers une page FRANÇAISE de Docs/ dont la jumelle anglaise existe désormais → lien vers la jumelle anglaise
  (et retrait des marques « (FR) », « (French) », « *FR* ») ;
- un lien vers une page ANGLAISE qui n'existe pas encore → lien vers la page française, marqué « (FR) ».
Les ancres des concepts transverses sont converties (titres traduits).
Côté français : ajoute « 🌐 [English](…) » aux pages qui ont une jumelle anglaise.

    python Docs/en/outils/liens-lot.py
"""
import os
import pathlib
import re

DOCS = pathlib.Path(__file__).resolve().parents[2]
EN = DOCS / "en"
RACINE = DOCS.parent

ANCRES = {  # concepts-transverses : ancre française → ancre anglaise
    "idempotence": "idempotency", "outbox": "outbox", "concurrence-optimiste": "optimistic-concurrency",
    "compensation-et-saga": "compensation-and-saga", "nouveaux-essais-et-lettres-mortes": "retries-and-dead-letters",
    "cohérence-à-terme": "eventual-consistency",
    "événements-de-domaine-vs-dintégration": "domain-vs-integration-events",
}
ANCRES_FR = {v: k for k, v in ANCRES.items()}
LIEN = re.compile(r"\[([^\]]+)\]\(([^)\s#]+\.md)(#[^)]*)?\)( \(\*FR\*\)| \(French\)| \(FR\))?")


def relatif(cible, depuis):
    return os.path.relpath(cible, depuis.parent).replace("\\", "/")


def traiter(page):
    t = page.read_text(encoding="utf-8")

    def remplacer(m):
        texte, lien, ancre, apres = m.group(1), m.group(2), m.group(3) or "", m.group(4) or ""
        cible = (page.parent / lien).resolve()
        if texte == "Français":
            # Le lien 🌐 vers la jumelle française doit toujours viser le français.
            return f"[{texte}]({relatif(DOCS / page.resolve().relative_to(EN), page)})" if page.is_relative_to(EN) else m.group(0)
        try:
            chemin_fr = cible.relative_to(DOCS)
        except ValueError:
            return m.group(0)
        if chemin_fr.parts[0] == "en":  # lien vers une page anglaise
            if cible.exists():
                return m.group(0)
            fr = DOCS / pathlib.Path(*chemin_fr.parts[1:])
            if not fr.exists():
                return m.group(0)
            if ancre and fr.name == "concepts-transverses.md":
                ancre = "#" + ANCRES_FR.get(ancre[1:], ancre[1:])
            return f"[{texte} (FR)]({relatif(fr, page)}{ancre})"
        jumelle = EN / chemin_fr
        if not jumelle.exists():
            return m.group(0)
        if ancre and jumelle.name == "concepts-transverses.md":
            ancre = "#" + ANCRES.get(ancre[1:], ancre[1:])
        texte = {"*FR*": "EN", "Full page (French)": "Full page"}.get(texte, re.sub(r" \((FR|French)\)$", "", texte))
        return f"[{texte}]({relatif(jumelle, page)}{ancre})"

    nouveau = LIEN.sub(remplacer, t)
    # README.en.md : « (details: [Docs/NN-x.md](…), French; » devient « (details: [Docs/en/NN-x.md](…); »
    nouveau = re.sub(r"\[Docs/([0-9]{2}-[a-z-]+\.md)\]\(Docs/en/\1\), French", r"[Docs/en/\1](Docs/en/\1)", nouveau)
    if nouveau != t:
        page.write_text(nouveau, encoding="utf-8", newline="\n")


for page in [RACINE / "README.en.md", *sorted(EN.rglob("*.md"))]:
    if page.name != "TRANSLATION.md":
        traiter(page)

# Côté français : lien 🌐 vers la jumelle anglaise
for fr in sorted(DOCS.rglob("*.md")):
    if fr.is_relative_to(EN):
        continue
    jumelle = EN / fr.relative_to(DOCS)
    t = fr.read_text(encoding="utf-8")
    if not jumelle.exists() or "🌐 [English]" in t:
        continue
    lien = f"🌐 [English]({relatif(jumelle, fr)})"
    lignes = t.split("\n")
    i = next((i for i, l in enumerate(lignes) if l.startswith(("📁 ", "⬅️ ", "🏠 "))), None)
    if i is not None:
        lignes[i] += f" · {lien}"
    else:
        lignes[1:1] = ["", lien]
    fr.write_text("\n".join(lignes), encoding="utf-8", newline="\n")
print("ok")

"""Après un lot de traduction : relie les pages anglaises existantes, renvoie vers le français celles qui manquent.

- Docs/en/NN-*.md : un lien « [x](NN-y.md) » vers une page anglaise absente → « [x (FR)](../NN-y.md) » ;
  un lien « [x (FR)](../NN-y.md) » vers une page désormais traduite → « [x](NN-y.md) ».
- Docs/en/fiches : « 📖 [Full page (French)](../../NN.md) » → « 📖 [Full page](../NN.md) » si la page existe.
- Docs/en/README.md : colonne « Full page » « [*FR*](../NN.md) » → « [EN](NN.md) » si la page existe.
- Docs/NN-*.md (français) : ajoute « 🌐 [English](en/NN.md) » à la ligne 📁 si la page anglaise existe.
"""
import pathlib, re

DOCS = pathlib.Path(__file__).resolve().parents[2]
EN = DOCS / "en"
existe = lambda nom: (EN / nom).exists()

for page in sorted(EN.glob("[0-9][0-9]-*.md")):
    t = page.read_text(encoding="utf-8")
    t = re.sub(r"\[([^\]]+)\]\(([0-9][0-9]-[^)#]+\.md)(#[^)]*)?\)",
               lambda m: m.group(0) if existe(m.group(2)) else f"[{m.group(1)} (FR)](../{m.group(2)}{m.group(3) or ''})", t)
    t = re.sub(r"\[([^\]]+) \(FR\)\]\(\.\./([0-9][0-9]-[^)#]+\.md)(#[^)]*)?\)",
               lambda m: f"[{m.group(1)}]({m.group(2)}{m.group(3) or ''})" if existe(m.group(2)) else m.group(0), t)
    page.write_text(t, encoding="utf-8", newline="\n")

for page in sorted((EN / "fiches").glob("[0-9][0-9]-*.md")):
    t = page.read_text(encoding="utf-8")
    t = re.sub(r"📖 \[Full page \(French\)\]\(\.\./\.\./([0-9][0-9]-[^)]+\.md)\)",
               lambda m: f"📖 [Full page](../{m.group(1)})" if existe(m.group(1)) else m.group(0), t)
    page.write_text(t, encoding="utf-8", newline="\n")

idx = EN / "README.md"
t = idx.read_text(encoding="utf-8")
t = re.sub(r"\[\*FR\*\]\(\.\./([0-9][0-9]-[^)]+\.md)\)",
           lambda m: f"[EN]({m.group(1)})" if existe(m.group(1)) else m.group(0), t)
# liens du texte de l'index (ex. « [demo 23](../23-….md) (*FR*) »)
t = re.sub(r"\[([^\]]+)\]\(\.\./([0-9][0-9]-[^)]+\.md)\) \(\*FR\*\)",
           lambda m: f"[{m.group(1)}]({m.group(2)})" if existe(m.group(2)) else m.group(0), t)
idx.write_text(t, encoding="utf-8", newline="\n")

for page in sorted(DOCS.glob("[0-9][0-9]-*.md")):
    t = page.read_text(encoding="utf-8")
    if not existe(page.name) or "🌐 [English]" in t:
        continue
    lignes = t.split("\n")
    i = next(i for i, l in enumerate(lignes) if l.startswith("📁 "))
    lignes[i] += f" · 🌐 [English](en/{page.name})"
    page.write_text("\n".join(lignes), encoding="utf-8", newline="\n")
print("ok")

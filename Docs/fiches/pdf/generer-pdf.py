"""Génère fiches-architectures.pdf : les fiches mémo, une par page A4, schémas Mermaid compris.

Construit une page HTML (Markdown converti par marked.js, schémas rendus par Mermaid, tous deux chargés depuis
jsDelivr), puis l'imprime en PDF avec Edge (ou Chrome) sans interface.

    python generer-pdf.py            # écrit fiches-architectures.html puis fiches-architectures.pdf
"""
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

ICI = Path(__file__).resolve().parent
FICHES = ICI.parent
HTML = ICI / "fiches-architectures.html"
PDF = ICI / "fiches-architectures.pdf"

# Chrome d'abord : lancé depuis l'app Claude Desktop (MSIX), Edge sans interface se termine sans rien écrire.
NAVIGATEURS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]


def lire_fiches():
    fiches = []
    for f in sorted(FICHES.glob("[0-9][0-9]-*.md")):
        texte = f.read_text(encoding="utf-8")
        # La ligne de navigation finale (⬅️ · index · ➡️) n'a pas de sens sur papier.
        texte = re.sub(r"\n---\n[^\n]*\n?$", "\n", texte.rstrip() + "\n")
        fiches.append({"id": "f" + f.name[:2], "md": texte})
    index = (FICHES / "README.md").read_text(encoding="utf-8")
    return index, fiches


PAGE = """<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<title>Architectures logicielles · fiches mémo</title>
<style>
  @page {
    size: A4;
    margin: 11mm 13mm 13mm 13mm;
    @bottom-left { content: "ArchitecturesDemo · fiches mémo"; font: 7.5pt "Segoe UI", sans-serif; color: #6e7781; }
    @bottom-right { content: counter(page) " / " counter(pages); font: 7.5pt "Segoe UI", sans-serif; color: #6e7781; }
  }
  @page :first { @bottom-left { content: none; } @bottom-right { content: none; } }
  :root { --accent: #0969da; --encre: #1f2328; --doux: #57606a; --trait: #d0d7de; --fond: #f6f8fa; }
  * { box-sizing: border-box; }
  html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  body { margin: 0; font: 9.4pt/1.42 "Segoe UI", system-ui, sans-serif; color: var(--encre); background: #fff; }
  section { break-before: page; }
  section:first-of-type { break-before: auto; }

  /* Couverture */
  .couverture { height: 268mm; display: flex; flex-direction: column; justify-content: center; padding: 0 10mm; }
  .couverture .sur { color: var(--accent); font-weight: 600; letter-spacing: .08em; text-transform: uppercase; font-size: 10pt; }
  .couverture h1 { font-size: 30pt; line-height: 1.1; margin: 4mm 0 6mm; border: 0; padding: 0; }
  .couverture p { font-size: 11.5pt; color: var(--doux); max-width: 150mm; margin: 0 0 3mm; }
  .couverture .filet { width: 40mm; height: 2.2mm; background: var(--accent); margin: 0 0 8mm; }
  .couverture .pied { margin-top: 18mm; font-size: 9pt; }

  h1 { font-size: 16.5pt; margin: 0 0 2.5mm; padding-bottom: 1.8mm; border-bottom: 2px solid var(--accent); }
  h2 { font-size: 11pt; color: var(--accent); margin: 3.5mm 0 1.5mm; }
  p { margin: 0 0 2mm; }
  ul, ol { margin: 0 0 2mm; padding-left: 5.5mm; }
  li { margin: 0 0 .8mm; }
  blockquote { margin: 0 0 2.5mm; padding: 2mm 3.5mm; background: #ddf4ff; border-left: 3px solid var(--accent); border-radius: 0 4px 4px 0; }
  blockquote p { margin: 0; font-size: 10pt; }
  code { font: 8.4pt Consolas, "Cascadia Mono", monospace; background: var(--fond); padding: 0 .6mm; border-radius: 2px; }
  table { border-collapse: collapse; width: 100%; margin: 0 0 2.5mm; font-size: 8.7pt; break-inside: avoid; }
  th, td { border: 1px solid var(--trait); padding: 1.1mm 2mm; text-align: left; vertical-align: top; }
  th { background: var(--fond); }
  hr { border: 0; border-top: 1px solid var(--trait); margin: 3mm 0; }
  a, .lien { color: inherit; text-decoration: none; }
  a[href^="#"] { color: var(--accent); }
  .schema { margin: 1mm 0 2.5mm; padding: 2mm; border: 1px solid var(--trait); border-radius: 4px; text-align: center; break-inside: avoid; }
  .schema svg { max-width: 100% !important; max-height: 82mm; height: auto; }
  .index { font-size: 8.8pt; }
  .index h1 { font-size: 16pt; }
  .index h2 { font-size: 10pt; margin: 2.6mm 0 1mm; }
  .index p, .index blockquote { margin-bottom: 1.5mm; }
  .index table { font-size: 8.3pt; margin-bottom: 1.5mm; }
  .index th, .index td { padding: .6mm 2mm; }
  .index hr { margin: 2mm 0; }
</style>
</head>
<body>
<section class="couverture">
  <div class="sur">ArchitecturesDemo · .NET 10</div>
  <h1>Architectures logicielles<br>Fiches mémo</h1>
  <div class="filet"></div>
  <p>25 démos, une page chacune : l'idée en une phrase, un schéma, les règles qui font l'architecture, quand l'utiliser,
     les pièges.</p>
  <p>Le même métier partout : une petite banque où Alice vire 250 € à Bob.</p>
  <p class="pied">Le détail (code, tests, déroulements) est dans <code>Docs/</code> du dépôt, une page complète par
     architecture ; la version sans code dans <code>Docs/pour-tous/</code>.</p>
</section>
<main id="contenu"></main>
<script src="https://cdn.jsdelivr.net/npm/marked@12.0.2/marked.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.min.js"></script>
<script>
const INDEX = __INDEX__;
const FICHES = __FICHES__;

function section(md, id, classe) {
  const s = document.createElement("section");
  s.id = id;
  if (classe) s.className = classe;
  s.innerHTML = marked.parse(md);
  // Liens : entre fiches → ancres internes ; vers le reste du dépôt → simple texte.
  for (const a of s.querySelectorAll("a")) {
    const href = a.getAttribute("href") || "";
    const fiche = href.match(/^(\\d\\d)-[^/]*\\.md$/);
    if (fiche) a.setAttribute("href", "#f" + fiche[1]);
    else if (href === "README.md") a.setAttribute("href", "#index");
    else { const t = document.createElement("span"); t.className = "lien"; t.innerHTML = a.innerHTML; a.replaceWith(t); }
  }
  for (const code of s.querySelectorAll("pre > code.language-mermaid")) {
    const div = document.createElement("div");
    div.className = "schema mermaid";
    div.textContent = code.textContent;
    code.parentElement.replaceWith(div);
  }
  return s;
}

async function construire() {
  const main = document.getElementById("contenu");
  main.append(section(INDEX, "index", "index"));
  for (const f of FICHES) main.append(section(f.md, f.id));
  mermaid.initialize({ startOnLoad: false, theme: "default", flowchart: { useMaxWidth: true }, securityLevel: "loose" });
  let n = 0;
  for (const div of document.querySelectorAll(".schema.mermaid")) {
    const { svg } = await mermaid.render("m" + (n++), div.textContent);
    div.innerHTML = svg;
  }
  document.body.dataset.pret = "oui";
  document.title = "PRET";
}
construire().catch(e => { document.title = "ERREUR " + e.message; console.error(e); });
</script>
</body>
</html>
"""


def main():
    index, fiches = lire_fiches()
    page = (PAGE.replace("__INDEX__", json.dumps(index, ensure_ascii=False))
                .replace("__FICHES__", json.dumps(fiches, ensure_ascii=False)))
    HTML.write_text(page, encoding="utf-8")
    print(f"{HTML.name} : {len(fiches)} fiches")

    PDF.unlink(missing_ok=True)
    navigateur = next((n for n in NAVIGATEURS if Path(n).exists()), None) or shutil.which("msedge")
    if not navigateur:
        sys.exit("Ni Edge ni Chrome trouvé : ouvrir le HTML et l'imprimer en PDF à la main.")
    profil = ICI / ".profil-impression"
    subprocess.run([
        navigateur, "--headless=new", "--disable-gpu", "--no-first-run", f"--user-data-dir={profil}",
        "--no-pdf-header-footer", "--virtual-time-budget=30000", "--run-all-compositor-stages-before-draw",
        f"--print-to-pdf={PDF}", HTML.as_uri(),
    ], check=True, timeout=180)
    shutil.rmtree(profil, ignore_errors=True)
    if not PDF.exists():
        sys.exit(f"{Path(navigateur).name} n'a pas écrit de PDF.")
    print(f"{PDF.name} : {PDF.stat().st_size // 1024} Ko")


if __name__ == "__main__":
    main()

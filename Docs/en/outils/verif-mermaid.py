"""Rend chaque schéma Mermaid des fichiers donnés dans Chrome sans interface et liste les erreurs de syntaxe."""
import json, pathlib, re, subprocess, sys, tempfile

fichiers = [pathlib.Path(a) for a in sys.argv[1:]]
blocs = []
for f in fichiers:
    for i, m in enumerate(re.finditer(r"```mermaid\n(.*?)```", f.read_text(encoding="utf-8"), re.S)):
        blocs.append({"id": f"{f.name}#{i + 1}", "code": m.group(1)})

page = """<!doctype html><meta charset="utf-8"><body><pre id="r">EN COURS</pre>
<script src="https://cdn.jsdelivr.net/npm/mermaid@11.4.1/dist/mermaid.min.js"></script>
<script>
const BLOCS = __BLOCS__;
(async () => {
  mermaid.initialize({ startOnLoad: false, securityLevel: "loose" });
  const erreurs = [];
  let n = 0;
  for (const b of BLOCS) {
    try { await mermaid.render("m" + (n++), b.code); }
    catch (e) { erreurs.push(b.id + " : " + (e.message || e)); }
  }
  document.getElementById("r").textContent = "FINI " + BLOCS.length + " schémas, " + erreurs.length + " erreurs\\n" + erreurs.join("\\n");
})();
</script>""".replace("__BLOCS__", json.dumps(blocs, ensure_ascii=False))

dossier = pathlib.Path(tempfile.mkdtemp())
html = dossier / "verif.html"
html.write_text(page, encoding="utf-8")
sortie = subprocess.run([r"C:\Program Files\Google\Chrome\Application\chrome.exe", "--headless=new", "--disable-gpu",
                         f"--user-data-dir={dossier / 'profil'}", "--virtual-time-budget=60000", "--dump-dom", html.as_uri()],
                        capture_output=True, timeout=180).stdout.decode("utf-8", "replace")
m = re.search(r'<pre id="r">(.*?)</pre>', sortie, re.S)
sys.stdout.buffer.write(((m.group(1) if m else sortie[:500]) + "\n").encode("utf-8"))

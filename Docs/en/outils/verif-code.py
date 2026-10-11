"""Vérifie que chaque bloc de code d'une page anglaise est identique à celui de sa jumelle française.

    python Docs/en/outils/verif-code.py
"""
import pathlib
import re

DOCS = pathlib.Path(__file__).resolve().parents[2]
motif = re.compile(r"```(csharp|xml|json|powershell|http|protobuf|graphql|cs)\n(.*?)```", re.S)
total = ecarts = 0
for en in sorted((DOCS / "en").rglob("*.md")):
    fr = DOCS / en.relative_to(DOCS / "en")
    if not fr.exists():
        continue
    a = [m.group(2) for m in motif.finditer(fr.read_text(encoding="utf-8"))]
    b = [m.group(2) for m in motif.finditer(en.read_text(encoding="utf-8"))]
    total += len(b)
    if a != b:
        ecarts += 1
        print(en.relative_to(DOCS), "FR", len(a), "EN", len(b))
print(total, "blocs de code comparés, pages avec écart :", ecarts)

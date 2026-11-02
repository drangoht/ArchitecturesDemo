# ArchitecturesDemo

Démos pédagogiques d'architectures logicielles (créées le 2026-10-10), dépôt **privé** `github.com/drangoht/ArchitecturesDemo`.
Le même métier bancaire implémenté 25 fois en .NET 10 (couches, hexagonale, Onion, Clean, cœur fonctionnel, Vertical Slice,
composants, monolithe modulaire, microservices, CQRS, Event Sourcing, DDD, serverless simulé, microkernel, saga orchestrée,
multi-tenant, BFF, cellules, acteurs, space-based, pipeline, chorégraphie, la migration Strangler Fig, trois styles d'API :
REST, gRPC, GraphQL, et deux patterns de présentation : MVC, MVVM), 489 tests xUnit. Documentation dans `Docs\` (une page par architecture, schémas Mermaid, extraits du code) ; le `README.md`
liste les démos, leurs ports (5100–5123, microservices 5200–5209, services du BFF 5210, cellules 5231–5234, ancien et nouveau
système du Strangler Fig 5240–5241, gRPC de la démo 23 5250, site MVC de la démo 24 5124) et les points à observer.
- `dotnet test ArchitecturesDemo.slnx` : tout compiler et tester (chaque démo a aussi sa propre `.slnx`).
- Aucun package hors du cache NuGet local (choix de l'utilisateur). **Code en anglais** (identifiants, commentaires, messages,
  routes, tests, dossiers `00-Layered`…`24-MvcMvvm`, projets) depuis le 2026-10-12 (lot 7) : la version française du code n'existe plus.
- Démo 23 : Protobuf, gRPC (HTTP/2, trailers) et le moteur GraphQL sont écrits à la main dans `Styles.Tooling` (pas de package).
- Démo 24 : `Ui.Wpf` cible `net10.0-windows` (WPF, Windows seulement) ; `Ui.Wpf.exe capture fichier.png` dessine la fenêtre
  dans une image sans l'afficher. Les tests (net10.0) vérifient les liaisons du XAML en lisant le fichier.
- `Docs\pour-tous\` : la même chose vulgarisée, sans code (une page par architecture, analogie de la vie courante, lexique),
  illustrée par des images SDXL générées avec le ComfyUI local (`images\generer.py`, base sans refiner ≈ 50 s/image).
- `Docs\fiches\` : une fiche mémo technique d'une page par architecture (phrase, schéma Mermaid, règles, quand l'utiliser,
  pièges), renvoyant à la page complète. À tenir à jour avec toute nouvelle démo, comme `pour-tous\`. PDF imprimable
  `Docs/fiches/pdf/fiches-architectures.pdf`, régénéré par `generer-pdf.py` (Chrome sans interface ; Edge échoue depuis l'app).
- `Docs\differences\` : 17 comparaisons détaillées deux à deux (Hexagonale ou Onion, CQRS ou Event Sourcing, Saga ou
  Chorégraphie…), extraits du code des démos ; REST/gRPC/GraphQL et MVC/MVVM renvoient à leurs démos (23, 24).
- Noms d'architecture : un nom anglais d'usage est gardé tel quel (pas de traduction inventée) ; un nom français affiche aussi
  son nom anglais (ligne « **Nom anglais** » sous le titre, colonne des sommaires) ;
  référence `Docs\noms-architectures.md`.
- **Version anglaise complète de la documentation** (2026-10-11), les deux langues gardées côte à côte : `README.en.md`
  et `Docs\en\` (même arborescence et mêmes noms de fichiers que `Docs\` : pages, fiches + PDF `generer-pdf.py --en`,
  pour-tous, differences, concepts, noms ; lien 🌐 vers la jumelle). Conventions, glossaire et outils de contrôle
  (`Docs\en\outils\` : liens-lot, verif-code, verif-mermaid) dans `Docs\en\TRANSLATION.md`. Toute modification d'une page
  doit toucher sa jumelle. Blocs de code identiques dans les deux langues (code anglais, `verif-code.py` : 0 écart) ; seule
  la prose suit la langue de la page.
- `13-Microkernel\plugins\` est une sortie de compilation (non versionnée) : compiler la solution avant de lancer l'API.

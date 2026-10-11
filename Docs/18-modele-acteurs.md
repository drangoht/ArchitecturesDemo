# 18. Modèle d'acteurs

> **En une phrase** : le système est fait de milliers de petits objets, les *acteurs*, qui ont chacun **leur propre état**,
> **ne partagent rien** et ne communiquent que **par messages**, traités **un par un** ; la concurrence ne demande plus
> aucun verrou, et une panne se répare en **remplaçant** l'acteur fautif.

📁 Code : [`18-ModeleActeurs`](../18-ModeleActeurs) · ⬅️ [Architecture cellulaire](17-cell-based.md) · ➡️ [Architecture spatiale](19-space-based.md)

---

## L'idée : ne plus partager l'état

Dans toutes les démos précédentes, l'état est **partagé** : plusieurs requêtes simultanées lisent et modifient les mêmes
comptes, et c'est la base de données (transactions, verrous, contrôle de concurrence) qui évite qu'elles se marchent
dessus. Le modèle d'acteurs (Carl Hewitt, 1973 ; popularisé par Erlang) prend le problème à l'envers : **chaque donnée a
un seul propriétaire**, et on ne lui parle qu'en déposant un message dans sa boîte aux lettres.

```mermaid
flowchart LR
    subgraph Partage["ÉTAT PARTAGÉ"]
        direction TB
        R1["Requête 1"] --> V{{"verrou"}}
        R2["Requête 2"] --> V
        R3["Requête 3"] --> V
        V --> S[("solde d'Alice")]
    end
    subgraph Acteurs["ACTEURS"]
        direction TB
        M1["Requête 1"] -->|message| B["boîte aux lettres<br/>①②③"]
        M2["Requête 2"] -->|message| B
        M3["Requête 3"] -->|message| B
        B --> A["acteur compte/alice<br/>solde : un simple champ"]
    end
    Partage ~~~ Acteurs
```

À gauche, la sûreté dépend de **chaque** morceau de code qui touche le solde : un oubli de verrou, et des mises à jour se
perdent. À droite, elle est **structurelle** : seul l'acteur touche son champ, et il ne traite qu'un message à la fois.
Ses messages s'accumulent dans sa boîte, mais des acteurs **différents** travaillent en parallèle, sur tous les cœurs.

Trois règles définissent un acteur. En réponse à un message, il peut :

1. **modifier son propre état** (et seulement le sien) ;
2. **envoyer des messages** à d'autres acteurs dont il connaît l'adresse ;
3. **créer** d'autres acteurs (ici, ils sont *virtuels* : on les adresse, le runtime les crée).

| | Partage de l'état | Une panne… |
|---|---|---|
| Démos 00 à 17 | données partagées, protégées par la base (transactions) | annule la transaction ; le processus continue |
| **Acteurs** | **rien** : chaque état a un seul propriétaire | **remplace l'acteur** fautif, qui recharge son état durable |

---

## Les composants

```mermaid
flowchart TB
    API["Acteurs.Api<br/>une route = un message"] -->|"Demander / Envoyer"| SYS["SystemeActeurs<br/>activation · distribution · supervision · passivation"]
    SYS --> C1["compte/alice<br/>boîte + état"]
    SYS --> C2["compte/bob<br/>boîte + état"]
    SYS --> V1["virement/v-42<br/>boîte + état"]
    V1 -->|"Debiter"| C1
    V1 -->|"Crediter"| C2
    C1 -.->|"écrit son état"| ST[("MagasinEtats<br/>une ligne par acteur")]
    C2 -.-> ST
    V1 -.-> ST
```

- **Le runtime** ([Acteurs.Noyau](../18-ModeleActeurs/Acteurs.Noyau), ≈ 300 lignes écrites pour la démo, sans package) :
  - [`Acteur`](../18-ModeleActeurs/Acteurs.Noyau/Acteur.cs) : la classe de base, avec `Demarrer` (recharger l'état),
    `Recevoir` (traiter un message) et `Arreter` ;
  - [`ReferenceActeur`](../18-ModeleActeurs/Acteurs.Noyau/ReferenceActeur.cs) : l'**adresse** d'un acteur (`compte/1111…`),
    jamais l'objet lui-même ; on y `Envoyer` un message (*tell*, sans réponse) ou on lui `Demander` (*ask*, avec réponse et délai) ;
  - [`SystemeActeurs`](../18-ModeleActeurs/Acteurs.Noyau/SystemeActeurs.cs) : une boîte aux lettres
    (`System.Threading.Channels`) et une boucle par acteur actif, la supervision, la passivation.
- **Le métier** ([Acteurs.Banque](../18-ModeleActeurs/Acteurs.Banque)) : [`CompteActeur`](../18-ModeleActeurs/Acteurs.Banque/CompteActeur.cs)
  (un par compte), [`VirementActeur`](../18-ModeleActeurs/Acteurs.Banque/VirementActeur.cs) (un par virement), des
  [messages](../18-ModeleActeurs/Acteurs.Banque/Messages.cs) immuables (`record`), un stockage durable et un injecteur de pannes.
- **L'API** ([Endpoints.cs](../18-ModeleActeurs/Acteurs.Api/Endpoints.cs)) ne lit ni n'écrit aucun état : chaque route
  envoie **un** message à **un** acteur et traduit sa réponse en code HTTP.

```csharp
app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, MontantRequete r, SystemeActeurs systeme) =>
    Http(await systeme.Compte(id).Demander(new Retirer(r.Montant))));
```

En production, on n'écrit pas ce runtime : **Orleans** (Microsoft, acteurs *virtuels*, le modèle suivi ici),
**Akka.NET** (portage d'Akka, hiérarchies de supervision à la Erlang) ou **Proto.Actor** font la même chose, **répartis sur
plusieurs machines** : l'adresse `compte/alice` reste la même, que l'acteur vive sur le serveur 1 ou le 7.

---

## 1. Un message à la fois : la concurrence sans verrou

Tout le modèle tient dans cette boucle : un seul lecteur par boîte aux lettres, qui **attend la fin** de chaque message
avant de prendre le suivant ([SystemeActeurs.cs](../18-ModeleActeurs/Acteurs.Noyau/SystemeActeurs.cs)).

```csharp
await foreach (var enveloppe in activation.BoiteAuxLettres.Reader.ReadAllAsync())
{
    ...
        // LE point clé du modèle : un seul message à la fois, chacun traité jusqu'au bout avant le suivant.
        reponse = await activation.Instance!.Recevoir(enveloppe.Message);
```

Le compte peut alors écrire le code le plus naïf qui soit : lire le solde, vérifier, modifier. Pas de `lock`, pas de
transaction, pas de version optimiste ([CompteActeur.cs](../18-ModeleActeurs/Acteurs.Banque/CompteActeur.cs)) :

```csharp
if (debit && montant > etat.Solde)
    return new Refus($"Solde insuffisant ({etat.Solde:0.00} €).");

var signe = debit ? -montant : montant;
etat.Solde += signe;
```

**Essai réel** : 1 000 retraits de 3 € lancés **en même temps** sur le compte d'Alice (1 000 €) :

```json
POST /demo/retraits-simultanes  { "compte": "1111…", "nombre": 1000, "montant": 3 }
{ "soldeAvant": 1000, "demandes": 1000, "acceptes": 333, "refuses": 667, "soldeFinal": 1, "soldeMinimumObserve": 1, "dureeMs": 231 }
```

Exactement 333 retraits acceptés (333 × 3 = 999 €), aucun en double, jamais de solde négatif. Le même code sans acteur,
appelé par 1 000 threads, perdrait des mises à jour.

Un refus métier (solde insuffisant) est une **réponse** comme une autre (`Refus`), pas une exception : l'acteur continue.
Les exceptions sont réservées aux **pannes** (voir § 3).

---

## 2. Converser plutôt que verrouiller : le virement

Un virement touche deux comptes, donc deux acteurs aux états séparés : il n'y a **pas de transaction commune**. Un
troisième acteur, `virement/{id}`, mène la conversation et note où il en est, comme l'orchestrateur de la
[saga](14-saga-orchestree.md), mais à l'échelle d'un seul virement.

```mermaid
sequenceDiagram
    participant API
    participant V as virement/v-42
    participant A as compte/alice
    participant B as compte/bob
    API->>V: ExecuterVirement(alice, bob, 100)
    V->>V: état EnCours (stocké)
    V->>A: Debiter(clé v-42, 100)
    A-->>V: CompteDto
    V->>V: état DebitFait (stocké)
    V->>B: Crediter(clé v-42, 100)
    alt bob accepte
        B-->>V: CompteDto
        V->>V: état Termine
    else bob refuse (compte inexistant)
        B-->>V: Refus
        V->>A: Crediter(clé v-42-annulation, 100)
        V->>V: état Annule
    end
    V-->>API: VirementDto
```

Trois propriétés découlent du modèle, sans code dédié :

- **Pas d'interblocage.** Les comptes ne demandent jamais rien à personne ; seul le virement attend leurs réponses.
  Avec des verrous, A→B et B→A simultanés s'interbloquent si l'on ne verrouille pas toujours dans le même ordre.
  Essai réel : 500 virements dans chaque sens, en même temps.

  ```json
  POST /demo/virements-croises  { "nombre": 500, "montant": 1 }
  { "virements": 1000, "termines": 572, "refuses": 428, "totalAvant": 501, "totalApres": 501, "dureeMs": 518 }
  ```

  (Alice n'avait plus qu'1 € après l'essai précédent, d'où les refus ; le total, lui, est conservé.)
- **L'identité fait l'idempotence.** Rejouer `virement/facture-42` réveille le **même** acteur, qui connaît déjà la
  réponse ; un virement fini (terminé, refusé ou annulé) ne recommence jamais :

  ```csharp
  if (_etat is { Statut: StatutVirement.Termine or StatutVirement.Refuse or StatutVirement.Annule })
      return Vue();  // déjà fini : même réponse, aucun effet
  ```

- **Une reprise sans double débit.** Si le virement plante après le débit, son état stocké dit `DebitFait` : rejoué,
  il saute le débit et reprend au crédit. Et même refait, le débit porte la clé `virement-v-42`, que le compte reconnaît :

  ```csharp
  if (cle is not null && etat.Operations.Any(o => o.Cle == cle))
      return Vue();
  ```

⚠ Le piège du modèle : un acteur qui **attend** une réponse ne traite rien d'autre pendant ce temps. Si
le compte A demandait quelque chose au compte B pendant que B demande quelque chose à A, ils s'attendraient
mutuellement (jusqu'au délai de la demande). D'où la règle suivie ici : **les demandes vont dans un seul sens**
(virement → comptes), jamais en cycle ; quand un acteur n'a pas besoin de la réponse, il `Envoyer` sans attendre.

---

## 3. « Let it crash » : la supervision

Plutôt que de prévoir chaque erreur dans le code métier, on laisse planter l'acteur et son **superviseur** le
**remplace** : l'incarnation fautive, avec un état en mémoire peut-être incohérent, est jetée ; une neuve recharge le
dernier état **écrit** et reprend les messages suivants, qui attendaient dans la boîte.

```mermaid
flowchart LR
    M["Message suivant<br/>de la boîte"] --> T{"Recevoir"}
    T -->|"réponse ou Refus"| OK(["Message suivant"])
    T -->|"exception"| S{"Pannes dans<br/>la dernière minute ?"}
    S -->|"3 au plus"| R["Jeter l'incarnation,<br/>en créer une neuve<br/>qui relit le stockage"]
    R --> OK
    S -->|"la 4e"| Q(["Arrêt : quarantaine,<br/>messages refusés (503)<br/>jusqu'à la fin de la fenêtre"])
```

```csharp
if (activation.Pannes.Count > strategie.MaxRedemarrages)
{
    await Arreter(activation, $"{activation.Pannes.Count} pannes en {strategie.Fenetre.TotalSeconds:0} s ({panne.Message})");
    return false;
}

await Desincarner(activation);
if (!await Incarner(activation))
```

Le cas le plus parlant : une panne **au pire moment**, après la modification du solde en mémoire mais avant son
écriture (`Etape.AvantSauvegarde`, armée par `POST /systeme/pannes`). Essai réel :

```text
POST /comptes/1111…/retrait { "montant": 1 }  → 500 « compte/1111… a planté en traitant ce message : panne simulée (AvantSauvegarde) »
GET  /comptes/1111…                           → 200, solde inchangé : le retrait n'a pas eu lieu
GET  /comptes/2222… (piégé 4 fois)            → 500 500 500 500 503
```

Pas de solde à moitié modifié, pas de code de nettoyage : la nouvelle incarnation repart de l'état durable. Au-delà de
3 redémarrages par minute, redémarrer ne sert plus à rien : l'acteur est **arrêté** et ses messages refusés (`503`)
jusqu'à la fin de la fenêtre. Les autres acteurs ne voient rien (Alice répond pendant que Bob est en quarantaine).

Une panne se **propage le long des demandes** : si le compte de Bob plante pendant un virement, la demande du virement
échoue, et le virement plante à son tour. Les deux sont redémarrés, et rejouer le virement le termine (test
`La_panne_d_un_compte_remonte_au_virement_qui_peut_etre_rejoue`).

Le runtime de la démo applique une **stratégie par type** d'acteur (le modèle d'Orleans, où le runtime supervise tout
le monde). Akka et Erlang vont plus loin : les acteurs forment un **arbre**, chaque parent supervise ses enfants et peut
**escalader** la panne à son propre parent.

Enfin, un acteur lent ne bloque que ses propres appelants : `Demander` a un délai (5 s), au-delà duquel l'API répond
`504`. Le message, lui, reste dans la boîte et sera traité.

---

## 4. Acteurs virtuels : activation et passivation

Dans Orleans, un acteur (un *grain*) **existe toujours** en principe : on n'a ni à le créer ni à le détruire. Le premier
message l'**active** (une *incarnation* en mémoire), l'inactivité le **passive** (sa mémoire est libérée, son état
stocké reste), le message suivant le réactive. On peut donc avoir des millions de comptes et n'en garder en mémoire que
ceux qui servent.

```text
POST /systeme/passivation         → { "passives": 1004 }      Alice, 9999… (touché par la compensation), 1 002 virements
GET  /systeme                     → { "actifs": 0, ... }
GET  /systeme/stockage?type=compte → ["compte/1111…", "compte/2222…"]   9999… n'a jamais été ouvert : rien de stocké
GET  /comptes/1111…               → 200, solde retrouvé
GET  /systeme                     → { "actifs": 1, ... }      seule Alice a été réveillée
```

La passivation automatique tourne toutes les 5 s et endort les acteurs inactifs depuis 30 s
([PassivationAutomatique.cs](../18-ModeleActeurs/Acteurs.Api/PassivationAutomatique.cs)). Un acteur qui a des messages
en attente ou en cours n'est jamais passivé.

La garantie centrale d'un runtime d'acteurs virtuels : **au plus une incarnation par acteur à la fois**. Le cas délicat
est un message qui arrive pendant qu'une passivation est en train d'arrêter l'ancienne incarnation : la nouvelle
l'attend avant de démarrer.

```csharp
// Si une incarnation précédente finit encore de s'arrêter (passivation), la nouvelle l'attend avant de démarrer :
// jamais deux incarnations du même acteur en même temps, même pendant une fraction de seconde.
_incarnationsFinissantes.Remove(chemin, out var precedente);
activation.Boucle = Task.Run(() => Executer(activation, precedente));
```

(Sur plusieurs machines, garantir cette unicité est le vrai travail d'Orleans : un annuaire réparti dit quel serveur
héberge quel acteur.)

---

## 5. Le prix : ce que les acteurs rendent difficile

- **Les lectures transversales.** « Lister tous les comptes », « total des dépôts » : il faudrait réveiller et
  interroger chaque acteur. L'API n'a d'ailleurs pas de `GET /comptes`. On construit à côté une **vue de lecture**
  alimentée par les événements des acteurs : c'est le [CQRS](09-cqrs.md), souvent associé aux acteurs.
- **Pas de transaction entre acteurs** : un processus à plusieurs acteurs s'écrit étape par étape, avec états stockés,
  idempotence et compensation (§ 2).
- **Les cycles de demandes** s'interbloquent (§ 2) ; les boîtes aux lettres peuvent **déborder** si un acteur reçoit plus
  qu'il ne traite (un compte très sollicité est un goulot : un message à la fois, c'est aussi une limite de débit).
- **La livraison** : un `Envoyer` vers un acteur en quarantaine est perdu (« lettre morte », comptée dans `/systeme`) ;
  entre machines, un message peut être perdu ou dupliqué. D'où les clés d'idempotence.
- **Le débogage** : le déroulement d'une requête est éclaté en messages asynchrones entre acteurs ; il faut un journal
  (ici `/systeme/journal`) et, en production, une traçabilité distribuée.

---

## Les tests

Deux niveaux : le runtime seul, avec un acteur-jouet instrumenté
([NoyauTests.cs](../18-ModeleActeurs/Acteurs.Tests/NoyauTests.cs)), puis le métier
([BanqueTests.cs](../18-ModeleActeurs/Acteurs.Tests/BanqueTests.cs)) et l'API
([ApiTests.cs](../18-ModeleActeurs/Acteurs.Tests/ApiTests.cs)). Une horloge manuelle fait avancer le temps pour la
passivation et la quarantaine. L'acteur-jouet incrémente son compteur de la façon la plus dangereuse qui soit :

```csharp
case Incrementer:
    var lu = _compteur;
    await Task.Yield();
    _compteur = lu + 1;
    return _compteur;
```

| Test | Ce qui est vérifié |
|---|---|
| `Un_acteur_traite_un_seul_message_a_la_fois` | 1 000 incréments simultanés : aucun perdu, jamais deux messages en cours |
| `Des_acteurs_differents_travaillent_en_parallele` | deux acteurs, deux messages en même temps |
| `Les_messages_d_un_expediteur_sont_traites_dans_l_ordre_d_envoi` | ordre de la boîte aux lettres |
| `Le_premier_message_active_l_acteur_et_l_inactivite_le_passive`, `Un_acteur_occupe_n_est_jamais_passive` | acteurs virtuels |
| `Jamais_deux_incarnations_du_meme_acteur_en_meme_temps` | messages et passivations entrechoqués, arrêts lents |
| `Une_panne_remplace_l_incarnation_sans_perdre_les_messages_suivants`, `Une_panne_ne_touche_pas_les_autres_acteurs` | supervision |
| `Trop_de_pannes_arretent_l_acteur_jusqu_a_la_fin_de_la_fenetre` | quarantaine, lettres mortes |
| `Une_demande_trop_longue_expire_sans_que_le_message_soit_perdu` | délai d'une demande |
| `Mille_retraits_simultanes_ne_rendent_jamais_le_solde_negatif` | 333 acceptés sur 1 000, solde 1 € |
| `Des_virements_croises_simultanes_ne_s_interbloquent_pas` | 200 + 200 virements, total conservé |
| `Une_panne_avant_la_sauvegarde_annule_le_retrait_en_cours` | l'état relu dans le stockage |
| `Un_virement_interrompu_apres_le_debit_se_termine_quand_on_le_rejoue`, `La_panne_d_un_compte_remonte_au_virement…` | reprise sans double débit |
| `Un_virement_vers_un_compte_inexistant_est_compense`, `Rejouer_un_virement_refuse_ne_credite_jamais_la_cible` | compensation, refus définitif |
| `Rejouer_un_virement_termine_renvoie_le_meme_resultat_sans_effet`, `Un_debit_rejoue_avec_la_meme_cle_n_est_applique_qu_une_fois` | idempotence : identité de l'acteur, clé de l'opération |
| `Les_refus_metier_sont_des_reponses_pas_des_pannes` | un refus ne redémarre rien |
| `Un_compte_passive_retrouve_son_solde_dans_le_stockage` | passivation, réveil d'un seul acteur |
| `Des_pannes_repetees_mettent_l_acteur_en_quarantaine`, `Une_lenteur_repond_504_sans_bloquer_les_autres_comptes` | `500`, `503`, `504` par HTTP |

Vérifiés par mutation : créer une incarnation par message, ne pas attendre l'incarnation précédente, garder
l'incarnation fautive après une panne, passiver un acteur occupé, supprimer la quarantaine, l'idempotence des débits,
la compensation ou le « virement déjà fini » font chacun échouer au moins un test.

---

## Bilan

| 👍 Points forts | 👎 Limites |
|---|---|
| **Concurrence sans verrou** : chaque état a un seul propriétaire | Pas de transaction entre acteurs : étapes, idempotence, compensation |
| Du code métier simple et séquentiel, à l'intérieur de chaque acteur | Lectures transversales difficiles (il faut une vue à part) |
| **Pannes locales** : on remplace l'acteur, l'état durable fait foi | Cycles de demandes = interblocages ; un acteur très sollicité = goulot |
| Passe à l'échelle : millions d'acteurs, répartis sur des machines | Débogage et traçabilité plus difficiles (tout est asynchrone) |

**Quand l'utiliser** : beaucoup d'entités indépendantes, chacune avec un état qui change souvent et des accès
concurrents : joueurs et parties d'un jeu en ligne (Halo utilise Orleans), objets connectés, sessions, paniers,
comptes, messageries (WhatsApp sur Erlang), systèmes télécoms. Moins adapté quand l'essentiel est fait de requêtes
transversales sur des données relationnelles. En .NET : **Orleans** pour des acteurs virtuels sans gestion du cycle de
vie, **Akka.NET** ou **Proto.Actor** pour un contrôle fin (hiérarchies de supervision, flux).

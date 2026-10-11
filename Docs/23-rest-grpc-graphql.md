# 23. REST, gRPC, GraphQL (styles d'API)

> **En une phrase** : la même banque exposée de trois façons, des **ressources** (REST), des **procédures** (gRPC) et un
> **graphe que le client interroge** (GraphQL) ; le métier ne bouge pas, ce qui change, c'est qui décide de la forme
> des réponses, ce qui circule sur le réseau, et ce que HTTP fait encore pour vous.

📁 Code : [`23-RestGrpcGraphQL`](../23-RestGrpcGraphQL) · ⬅️ [Strangler Fig](22-strangler-fig.md) · ➡️ [MVC et MVVM](24-mvc-mvvm.md) · 🌐 [English](en/23-rest-grpc-graphql.md)

---

## L'idée : trois présentations du même métier

Les autres démos changent l'**intérieur** du système. Celle-ci garde un intérieur simple et unique, le
[ServiceBancaire](../23-RestGrpcGraphQL/Styles.Banque/ServiceBancaire.cs), et change sa **surface** : trois API dans le
même processus, chacune une fine couche de traduction. Un test d'architecture vérifie que le métier ignore tout des
trois (`Le_metier_ignore_tout_des_styles_d_API`).

```mermaid
flowchart LR
    APP(["Application mobile<br/>(Styles.Client)"])
    subgraph SRV["Styles.Serveur"]
        direction TB
        R["REST<br/>:5123 · HTTP/1.1<br/>/clients, /comptes, /virements"]
        Q["GraphQL<br/>:5123 · HTTP/1.1<br/>POST /graphql"]
        G["gRPC<br/>:5250 · HTTP/2<br/>banque.Banque/…"]
    end
    M["ServiceBancaire<br/>(Styles.Banque)"]
    APP -->|"JSON"| R
    APP -->|"JSON"| Q
    APP -->|"Protobuf"| G
    R --> M
    Q --> M
    G --> M
```

| | **REST** | **gRPC** | **GraphQL** |
|---|---|---|---|
| On manipule… | des **ressources** (noms), avec les verbes HTTP | des **procédures** (verbes) d'un service | un **graphe** de types, qu'on parcourt |
| Le contrat | implicite, ou décrit à part (OpenAPI) | **banque.proto**, dont on génère client et serveur | le **schéma** (SDL), vérifié à chaque requête |
| Qui choisit la forme de la réponse | le serveur, une représentation par ressource | le serveur, un message par méthode | **le client**, champ par champ |
| Format | JSON (texte, noms de champs) | Protobuf (binaire, numéros de champs) | JSON, exactement la forme demandée |
| Transport | HTTP, n'importe quelle version | **HTTP/2 obligatoire** (trailers) | HTTP, presque toujours `POST /graphql` |
| Erreurs | statut HTTP + `problem+json` | 17 statuts gRPC, dans les trailers | `200` + `errors`, à côté des données obtenues |
| Cache HTTP | **oui** (GET, ETag, 304) | non | non (en pratique) |
| Le serveur pousse | non (on interroge, ou SSE) | **flux** natifs | abonnements (WebSocket ; non fait ici) |
| Depuis un navigateur | oui | non (il faut gRPC-Web et un relais) | oui |
| Faire évoluer | ajouter des champs, sinon `/v2` | numéros de champs, champs inconnus ignorés | `@deprecated`, et on sait qui lit quoi |

---

## Les composants

- **Le métier** ([Styles.Banque](../23-RestGrpcGraphQL/Styles.Banque)) : clients, comptes, opérations, virements, et
  ce dont les trois API ont besoin sans être propre à l'une d'elles : des lectures **unitaires et groupées**, un
  abonnement aux nouvelles opérations, un compteur de lectures « en base ».
- **L'outillage** ([Styles.Outillage](../23-RestGrpcGraphQL/Styles.Outillage)) : faute de packages dans le cache NuGet
  local, ce que fourniraient Google.Protobuf, Grpc.AspNetCore et HotChocolate, écrit à la main et réduit à l'essentiel,
  mais fidèle aux formats filaires : [Protobuf.cs](../23-RestGrpcGraphQL/Styles.Outillage/Protobuf/Protobuf.cs)
  (encodage), [Grpc.cs](../23-RestGrpcGraphQL/Styles.Outillage/Grpc/Grpc.cs) (trames, statuts, échéances),
  [HoteGrpc.cs](../23-RestGrpcGraphQL/Styles.Outillage/Grpc/HoteGrpc.cs) et
  [ClientGrpc.cs](../23-RestGrpcGraphQL/Styles.Outillage/Grpc/ClientGrpc.cs), et un mini-moteur GraphQL
  ([Analyseur.cs](../23-RestGrpcGraphQL/Styles.Outillage/GraphQL/Analyseur.cs),
  [Schema.cs](../23-RestGrpcGraphQL/Styles.Outillage/GraphQL/Schema.cs),
  [Moteur.cs](../23-RestGrpcGraphQL/Styles.Outillage/GraphQL/Moteur.cs)). Écrire ces pièces montre ce que sont
  vraiment ces protocoles : quelques centaines de lignes chacun.
- **Les contrats gRPC** ([banque.proto](../23-RestGrpcGraphQL/Styles.Contrats/banque.proto) et
  [Messages.cs](../23-RestGrpcGraphQL/Styles.Contrats/Messages.cs)) : sans `protoc`, les classes sont écrites à la main
  et un test vérifie qu'elles correspondent au `.proto` champ par champ.
- **Le serveur** ([ApiRest.cs](../23-RestGrpcGraphQL/Styles.Serveur/ApiRest.cs),
  [ApiGrpc.cs](../23-RestGrpcGraphQL/Styles.Serveur/ApiGrpc.cs),
  [ApiGraphQL.cs](../23-RestGrpcGraphQL/Styles.Serveur/ApiGraphQL.cs)) et la table de
  [Traductions.cs](../23-RestGrpcGraphQL/Styles.Serveur/Traductions.cs) des erreurs.
- **Le client de comparaison** ([ClientsBanque.cs](../23-RestGrpcGraphQL/Styles.Client/ClientsBanque.cs)) : la même
  application écrite trois fois, derrière une interface `IClientBanque`, avec un compteur de requêtes et d'octets
  ([Mesure.cs](../23-RestGrpcGraphQL/Styles.Client/Mesure.cs)). Les tests l'utilisent aussi.

Données : **Alice** (`alice`) a trois comptes, courant `10001`, Livret A `10002` et compte joint `10003` ; **Bob**
(`bob`) un compte courant `20001`.

---

## 1. L'écran d'accueil : cinq requêtes, une, une

L'application mobile affiche, pour chaque compte, son libellé, son solde et ses trois dernières opérations. **Essai réel**
(`dotnet run --project Styles.Client`, avec 50 ms de latence simulée par requête, comme un réseau mobile) :

```text
  REST     5 requête(s), 0 o envoyés, 2014 o reçus, 178 ms
  GraphQL  1 requête(s), 210 o envoyés, 881 o reçus, 60 ms
  gRPC     1 requête(s), 14 o envoyés, 378 o reçus, 62 ms
```

**REST** suit les ressources : le client, ses comptes, puis les opérations de **chaque** compte (« 1 + N »). Les N
dernières partent en parallèle, mais ce sont trois vagues d'allers-retours, et une requête par compte :

```csharp
var client = await http.GetFromJsonAsync<JsonObject>($"/clients/{clientId}");
var comptes = await http.GetFromJsonAsync<JsonArray>($"/clients/{clientId}/comptes");
// 1 + 1 + N : une requête par compte pour ses opérations (en parallèle, mais une requête chacune).
```

**GraphQL** envoie la description de l'écran ; la réponse a exactement sa forme :

```graphql
query EcranAccueil($id: ID!, $n: Int) {
  client(id: $id) {
    nom
    comptes { numero libelle solde operations(derniers: $n) { date libelle montant } }
  }
}
```

**gRPC** a une procédure taillée pour cet écran, déclarée dans le contrat :

```protobuf
rpc ObtenirTableauDeBord (DemandeTableauDeBord) returns (TableauDeBord);
```

```mermaid
sequenceDiagram
    participant A as Application
    participant S as Serveur
    Note over A,S: REST (3 vagues, 5 requêtes)
    A->>S: GET /clients/alice
    A->>S: GET /clients/alice/comptes
    par une par compte
        A->>S: GET /comptes/10001/operations
        A->>S: GET /comptes/10002/operations
        A->>S: GET /comptes/10003/operations
    end
    Note over A,S: GraphQL ou gRPC (1 requête)
    A->>S: POST /graphql · ou · banque.Banque/ObtenirTableauDeBord
```

Les trois écrans obtenus sont **identiques** (test `Les_trois_API_donnent_exactement_le_meme_ecran`). REST aurait pu
faire mieux, avec une ressource dédiée (`/clients/alice/tableau-de-bord`) ou un paramètre `?expand=operations` : c'est
exactement ce que fait un [BFF](16-bff.md). Mais il faut alors une ressource par écran, ce que GraphQL évite.

---

## 2. Ce qui circule

Pour le même écran, REST reçoit **2,3 fois** plus que GraphQL et **5 fois** plus que gRPC (corps seulement ; test
`gRPC_transfere_le_moins_d_octets_et_REST_le_plus`).

**REST** renvoie la représentation complète de chaque compte, la même pour tous les clients. L'écran voulait deux
champs ; il en reçoit dix (sur-récupération, test `La_representation_d_un_compte_est_complete_et_la_meme_pour_tous_les_clients`) :

```json
{"numero":"10001","libelle":"Compte courant","type":"courant","iban":"FR76 3000 4000 0110 0010 0010 001","solde":1626.05,
 "devise":"EUR","dateOuverture":"2015-03-02","plafondDecouvert":500,"titulaire":{"id":"alice","nom":"Alice Martin"},
 "_links":{"self":"/comptes/10001","operations":"/comptes/10001/operations","titulaire":"/clients/alice"}}
```

**GraphQL** ne renvoie que les champs demandés, mais en JSON : chaque nom de champ est répété dans chaque objet. Et
c'est lui qui **envoie** le plus (210 octets) : le texte de la requête part à chaque appel. (En production, les
« requêtes persistées » remplacent ce texte par un identifiant.)

**gRPC** ne transmet ni noms ni texte : des numéros de champs et des entiers binaires. L'exemple de la documentation
Protobuf (test `Un_entier_s_encode_en_varint_comme_dans_la_documentation_Protobuf`) :

```text
champ 1 = 150   →   08 96 01
                    08    = numéro de champ 1, type filaire 0 (varint)
                    96 01 = 150, sur 7 bits par octet, poids faibles d'abord
```

Deux conséquences visibles dans le contrat : une valeur par défaut (0, chaîne vide) **ne circule pas du tout** ; et il
n'y a **pas de type décimal**, d'où des montants en centimes :

```protobuf
// Pas de type décimal en Protobuf : les montants circulent en centimes, sur des entiers.
message Compte {
  string numero = 1;
  string libelle = 2;
  TypeCompte type = 3;
  int64 solde_centimes = 4;
  repeated Operation dernieres_operations = 5;
}
```

GraphQL non plus n'a pas de décimal : `Float` est un double (`"Float" => JsonValue.Create(Convert.ToDouble(valeur))`). Pour
de l'argent, on définit d'ordinaire un scalaire personnalisé. Et chaque format a sa précision pour les dates : l'essai
réel a montré un écran GraphQL différent des deux autres, parce que le schéma datait à la seconde quand REST gardait les
fractions. Le métier date donc désormais ses opérations à la seconde :

```csharp
maintenant = maintenant.AddTicks(-(maintenant.Ticks % TimeSpan.TicksPerSecond));
```

---

## 3. Le N+1 : déplacé, pas supprimé

Une seule requête GraphQL peut cacher **N** lectures en base : si le champ `operations` est résolu compte par compte,
`client { comptes { operations } }` lit la base une fois par compte, et `client { comptes { titulaire { comptes {
operations } } } }` neuf fois. Le client ne voit qu'un aller-retour ; la base, elle, voit tout.

Le moteur exécute donc la requête **par niveaux** : un champ est résolu pour tous les objets d'un même niveau à la fois.
Un résolveur **groupé** (le « DataLoader » des moteurs GraphQL) reçoit tous les comptes et fait une seule lecture :

```csharp
.ChampGroupe("operations", "[Operation!]!", (comptes, a) =>
{
    var parCompte = banque.DernieresOperations(comptes.Select(c => c.Numero).ToList(), a.LireOuDefaut<int?>("derniers") ?? 10);
    return comptes.Select(c => (object?)parCompte[c.Numero]).ToList();
}, new Arg("derniers", "Int", 10))
```

| Pour l'écran d'Alice | REST | GraphQL | gRPC |
|---|---|---|---|
| Requêtes HTTP | 5 | 1 | 1 |
| Lectures des opérations en base | **3** | **1** | **1** |

REST fait autant de lectures que de requêtes : le N+1 est **visible** (dans les journaux d'accès, dans la latence). En
GraphQL il est **invisible** et dépend de chaque résolveur. gRPC, avec sa méthode faite pour l'écran, le règle une fois
pour toutes côté serveur. Tests `Derriere_la_requete_unique_de_GraphQL_la_base_n_est_lue_qu_une_fois` et
`Les_operations_de_tous_les_comptes_d_un_niveau_sont_lues_en_une_seule_fois` (9 comptes, une lecture).

---

## 4. Une erreur, trois langues

Un virement refusé pour solde insuffisant, **essai réel** :

```text
  REST     422 SOLDE_INSUFFISANT  « Solde insuffisant : 1160,01 € disponibles (découvert autorisé : 300,00 €). »
  GraphQL  SOLDE_INSUFFISANT      « Solde insuffisant : 1160,01 € disponibles (découvert autorisé : 300,00 €). »
  gRPC     FailedPrecondition     « Solde insuffisant : 1160,01 € disponibles (découvert autorisé : 300,00 €). »
```

La traduction est écrite une fois, dans [Traductions.cs](../23-RestGrpcGraphQL/Styles.Serveur/Traductions.cs) :

| Erreur métier | REST | gRPC | GraphQL (`extensions.code`) |
|---|---|---|---|
| Client, compte ou virement inconnu | `404 Not Found` | `NotFound` (5) | `COMPTE_INCONNU`… |
| Montant invalide, même compte, paramètre invalide | `400 Bad Request` | `InvalidArgument` (3) | `MONTANT_INVALIDE`… |
| Solde insuffisant | `422 Unprocessable Entity` | `FailedPrecondition` (9) | `SOLDE_INSUFFISANT` |

- **REST** : le statut dit la catégorie, que comprennent tous les outils (caches, journaux, alertes sur les 5xx) ; le
  corps `application/problem+json` (RFC 9457) porte le détail.
- **gRPC** : la réponse HTTP est **toujours 200**. Le vrai statut arrive à la fin, dans les **trailers** HTTP/2
  (`grpc-status`, `grpc-message`), après le dernier message : c'est la raison pour laquelle gRPC exige HTTP/2. Le
  serveur refuse un appel sans trailers (`426`). Les 17 statuts sont des catégories, pas des codes métier : pour
  distinguer deux `FailedPrecondition`, il faut les détails riches (`google.rpc.Status`, non faits ici).
- **GraphQL** : `200` aussi, et l'erreur dans `errors`, avec le **chemin** du champ fautif. Les autres champs sont
  servis (test `Une_erreur_sur_un_champ_n_empeche_pas_d_obtenir_les_autres`) :

```json
{"data":{"a":{"libelle":"Compte courant"},"b":null},
 "errors":[{"message":"Compte « 99999 » inconnu.","locations":[{"line":1,"column":42}],"path":["b"],"extensions":{"code":"COMPTE_INCONNU"}}]}
```

Quand le champ en erreur est **non nul** (`!`), il ne peut pas valoir `null` : c'est son parent qui devient `null`, et
ainsi de suite jusqu'au premier champ qui l'accepte. Avec `operations(derniers: 51)` (refusé), le compte, la liste des
comptes et finalement `client` valent `null`, avec une erreur par compte (test
`Un_champ_non_nul_en_erreur_rend_nul_le_premier_parent_nullable`). Concevoir un schéma, c'est aussi choisir où ces
erreurs s'arrêtent. Enfin, une exception imprévue ne doit rien divulguer : `"Erreur interne."`, sans détail
(test `Une_erreur_inattendue_ne_divulgue_rien_au_client`).

---

## 5. Pousser plutôt qu'interroger

**gRPC** sait faire répondre le serveur plusieurs fois : `SuivreOperations` garde la réponse HTTP/2 ouverte et y écrit
chaque nouvelle opération, une trame à la fois.

```csharp
await foreach (var operation in operations.ReadAllAsync(annulation))
    await ecrire(Message(operation));
```

Le client fixe une **échéance** (en-tête `grpc-timeout`) ; le serveur l'applique aussi, et arrête le travail : le flux se
termine en `DeadlineExceeded` et l'abonnement est libéré (test `L_echeance_du_client_arrete_aussi_le_travail_cote_serveur`).
Un client qui part libère aussi le serveur (test `Un_client_qui_abandonne_libere_le_serveur`). **Essai réel** :

```text
  gRPC (flux serveur, 1 requête ouverte 2 s) :
      Épargne via REST de 10002 (10,00 €)
      Épargne via GraphQL de 10002 (10,00 €)
      Épargne via gRPC de 10002 (10,00 €)
      fin du flux : DeadlineExceeded
```

**REST** n'a pas de flux : on interroge. Mais HTTP rend l'interrogation peu coûteuse : chaque compte a un **ETag** qui
change à chaque opération ; le client renvoie celui qu'il a (`If-None-Match`) et reçoit `304 Not Modified`, sans corps,
tant que rien n'a bougé (test `L_ETag_evite_de_retransferer_un_compte_qui_n_a_pas_change`) :

```text
  REST (interrogation avec ETag, 5 fois) : 1 × 200, 4 × 304 Not Modified — 5 requête(s), 0 o envoyés, 341 o reçus
```

C'est le grand atout de REST, absent des deux autres : GET est **sûr et idempotent**, donc tout ce qui se trouve entre
le client et le serveur (navigateur, proxy, CDN) peut mettre en cache. GraphQL passe tout par `POST /graphql` et perd ce
cache ; gRPC aussi. (Pour pousser, REST a les *Server-Sent Events* ou les WebSockets ; GraphQL, les abonnements, sur
WebSocket : ni l'un ni l'autre n'est fait ici.)

---

## 6. Faire évoluer le contrat

Les trois styles laissent ajouter un champ sans casser les clients, chacun à sa façon.

- **gRPC** : seul le **numéro** de champ circule. On peut renommer un champ ; jamais changer ni réutiliser son numéro.
  Un client ancien **saute** les champs qu'il ne connaît pas, un client récent lit des valeurs par défaut dans un message
  ancien (tests `Un_ancien_client_ignore_les_champs_ajoutes_depuis`,
  `Un_nouveau_client_lit_un_ancien_message_avec_les_valeurs_par_defaut`). D'où la valeur 0 de chaque enum, réservée à
  « inconnu » (`TYPE_COMPTE_INCONNU = 0`).
- **GraphQL** : le client ne reçoit que ce qu'il demande, donc ajouter est toujours sûr. Retirer se prépare : le champ
  `soldeTexte` (le solde déjà formaté, de la première version) est **déprécié** dans le schéma publié, et le serveur
  **compte qui le demande encore** (`GET /graphql/utilisation`) ; on le retire le jour où le compteur ne bouge plus.
  Aucun autre style ne sait aussi précisément ce que lisent ses clients.

```graphql
soldeTexte: String! @deprecated(reason: "Utiliser « solde » (un nombre) et le formater à l'affichage.")
```

- **REST** : ajouter un champ JSON est sûr si les clients ignorent ceux qu'ils ne connaissent pas (c'est le
  comportement par défaut de System.Text.Json) ; pour un changement incompatible, on publie une nouvelle version
  (`/v2/comptes`, ou un en-tête), et on fait vivre les deux.

Sans `protoc`, un piège guette : des classes qui divergent du `.proto`. Le test
`Chaque_message_du_proto_correspond_a_sa_classe_champ_par_champ` compare nom, numéro et type de chaque champ, et
`Le_serveur_sert_chaque_methode_du_service_et_rien_d_autre` les méthodes.

---

## 7. Se protéger d'un client trop curieux

En REST et en gRPC, le serveur sait ce que coûte chaque appel : il les a écrits. En GraphQL, **le client écrit la
requête** : `client { comptes { titulaire { comptes { titulaire { … } } } } }` peut faire travailler le serveur sans
fin. Le moteur **valide** donc toute la requête contre le schéma **avant** d'exécuter quoi que ce soit (champs et
arguments inconnus, arguments obligatoires, types des variables, avec leur ligne et leur colonne), et refuse au-delà de
6 niveaux (tests `Une_requete_trop_profonde_est_refusee_avant_d_etre_executee`,
`Un_champ_ou_un_argument_inconnu_est_refuse_par_la_validation_avec_sa_position`). En production on ajoute un **coût
maximal** par requête, des limites sur la taille des listes (ici `derniers` ≤ 50), et souvent une liste blanche de
requêtes connues.

---

## Les tests

49 tests ([Styles.Tests](../23-RestGrpcGraphQL/Styles.Tests)) : les trois clients et le serveur en mémoire (le serveur
de test gère HTTP/2 et les trailers : gRPC passe aussi), le moteur GraphQL appelé directement, l'encodeur Protobuf seul.

| Test | Ce qui est vérifié |
|---|---|
| `Les_trois_API_donnent_exactement_le_meme_ecran`, `Un_virement_reussit_de_la_meme_facon_par_les_trois_API` | même métier, mêmes résultats |
| `REST_fait_une_requete_par_compte_GraphQL_et_gRPC_une_seule_en_tout`, `gRPC_transfere_le_moins_d_octets_et_REST_le_plus` | coûts réseau |
| `Derriere_la_requete_unique_de_GraphQL_la_base_n_est_lue_qu_une_fois` | N+1 en base |
| `La_meme_erreur_metier_dans_la_langue_de_chaque_API` (3 cas) | traductions des erreurs |
| `Un_entier_s_encode_en_varint_comme_dans_la_documentation_Protobuf`, `Une_chaine_est_precedee_de_sa_longueur`, `Les_valeurs_par_defaut_ne_circulent_pas`, `Le_zigzag_garde_les_petits_negatifs_courts`… | encodage Protobuf, octet par octet |
| `Un_ancien_client_ignore_les_champs_ajoutes_depuis`, `Un_nouveau_client_lit_un_ancien_message_avec_les_valeurs_par_defaut` | évolution du contrat gRPC |
| `Chaque_message_du_proto_correspond_a_sa_classe_champ_par_champ`, `L_enumeration_du_proto_correspond_a_celle_du_code`, `Le_serveur_sert_chaque_methode_du_service_et_rien_d_autre` | le `.proto` reste la source de vérité |
| `Un_appel_unaire_est_un_POST_dont_le_corps_est_un_message_encadre_et_le_statut_dans_les_trailers`, `Une_erreur_metier_repond_quand_meme_200_avec_le_statut_gRPC_dans_les_trailers` | le format filaire gRPC, vu « à la main » |
| `Le_flux_pousse_chaque_operation_des_qu_elle_a_lieu`, `L_echeance_du_client_arrete_aussi_le_travail_cote_serveur`, `Un_client_qui_abandonne_libere_le_serveur` | flux, échéances, annulation |
| `Le_client_obtient_exactement_les_champs_demandes_et_rien_d_autre`, `Des_alias_permettent_de_demander_deux_fois_le_meme_champ`, `Les_variables_sont_verifiees_selon_leur_type`, `Une_mutation_s_execute_et_rend_ce_que_le_client_demande` | GraphQL |
| `Une_erreur_sur_un_champ_n_empeche_pas_d_obtenir_les_autres`, `Un_champ_non_nul_en_erreur_rend_nul_le_premier_parent_nullable`, `Une_erreur_inattendue_ne_divulgue_rien_au_client` | réponses partielles, propagation des `null` |
| `Le_schema_publie_signale_les_champs_deprecies_et_le_serveur_sait_qui_les_utilise` | dépréciation et statistique d'utilisation |
| `Les_erreurs_sont_des_statuts_HTTP_avec_un_corps_problem_json` (4 cas), `Un_virement_cree_une_ressource_201_dont_l_adresse_est_dans_Location`, `L_ETag_evite_de_retransferer_un_compte_qui_n_a_pas_change` | REST |
| `Le_metier_ignore_tout_des_styles_d_API`, `L_outillage_ignore_la_banque_et_les_contrats_ignorent_le_metier` | **tests d'architecture** |

Vérifiés par mutation (25 essais, tous détectés) : valeurs par défaut transmises, champ inconnu refusé, zigzag absent,
saut de champ qui lit l'ancienne position ; erreur gRPC rendue comme un succès, échéance ignorée, type de contenu non
contrôlé, trailers ignorés par le client, méthode gRPC qui lit compte par compte ; abonnement jamais libéré, dates non
arrondies ; résolveur GraphQL non groupé, pas de limite de profondeur, champ inconnu accepté, erreur non absorbée par un
champ nullable, null non propagé, erreur interne divulguée, variable inutilisée ou mal typée acceptée, statistique
d'utilisation absente ; ETag ignoré, 200 au lieu de 201 ; traductions d'erreurs faussées ; numéro de champ changé dans
une classe sans toucher au `.proto`.

Deux de ces garanties viennent d'échecs réels pendant l'écriture de la démo : le saut des champs inconnus
(`position += LireVarint(octets, ref position)` lit l'ancienne valeur de `position` **avant** l'appel, et saute au
mauvais endroit : trouvé par `Un_ancien_client_ignore_les_champs_ajoutes_depuis`) et l'arrondi des dates (trouvé à
l'essai réel, section 2).

---

## Bilan

| | 👍 Points forts | 👎 Limites |
|---|---|---|
| **REST** | Simple, universel, lisible avec `curl` ; **le cache HTTP** ; les statuts compris par tous les outils | Une forme par ressource : trop de données, ou trop d'allers-retours ; pas de flux ; contrat souvent implicite |
| **gRPC** | **Compact et rapide** ; contrat strict et généré ; flux dans les deux sens ; échéances propagées | HTTP/2 obligatoire, pas de navigateur sans relais ; binaire illisible sans outil ; pas de cache HTTP |
| **GraphQL** | **Le client choisit** : un aller-retour par écran, rien de superflu ; schéma typé et introspectable ; on sait qui lit quoi | N+1 caché, requêtes coûteuses à borner, cache HTTP perdu, `200` pour tout ; un moteur à exploiter |

**Quand choisir quoi** : **REST** pour une API publique ou partenaire, des ressources simples, quand le cache compte ;
**gRPC** entre services internes (microservices, [démo 8](08-microservices.md)), pour le débit, la latence, les flux et
les contrats stricts entre équipes ; **GraphQL** devant des clients nombreux et variés aux écrans riches (mobile, web),
souvent comme couche d'agrégation au-dessus de services REST ou gRPC, là où l'on écrirait sinon un
[BFF](16-bff.md) par application. Les trois cohabitent souvent : gRPC entre les services, GraphQL ou REST vers
l'extérieur. En production : **ASP.NET Core** (Minimal API) et **OpenAPI** pour REST ; **Grpc.AspNetCore**,
**Grpc.Net.Client** et **Grpc.Tools** (`protoc`) pour gRPC, avec **gRPC-Web** ou le transcodage JSON pour les
navigateurs ; **HotChocolate** ou **GraphQL.NET** pour GraphQL, avec leurs DataLoaders, l'analyse de coût et les
requêtes persistées.

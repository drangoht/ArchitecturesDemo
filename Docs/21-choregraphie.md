# 21. Chorégraphie (événementiel pur)

**Nom anglais** : *Event-Driven Choreography*

> **En une phrase** : aucun service ne conduit le processus ; chacun **réagit aux événements** des autres et publie les
> siens, et le virement avance de réaction en réaction, comme des danseurs qui suivent la musique sans chef d'orchestre.

📁 Code : [`21-Choregraphie`](../21-Choregraphie) · ⬅️ [Pipeline](20-pipeline.md) · ➡️ [Strangler Fig](22-strangler-fig.md)

---

## L'idée : le même virement que la démo 14, sans orchestrateur

La [saga orchestrée](14-saga-orchestree.md) confie le processus à un orchestrateur qui appelle chaque service à son tour.
Ici, le **même processus**, avec les mêmes données (Alice 50 000 €, Bob 500 €, Carla 1 000 $) et les mêmes règles
(analyste au-delà de 10 000 €, mêmes taux de change), est écrit… **nulle part**. Il émerge des abonnements :

```mermaid
flowchart LR
    API(["API"]) -->|"VirementDemande"| CO["Comptes<br/>réserve"]
    CO -->|"FondsReserves"| CF["Conformité<br/>examine"]
    CF -->|"ConformiteApprouvee"| CH["Change<br/>convertit"]
    CH -->|"DevisesConverties"| CO2["Comptes<br/>règle"]
    CO2 -->|"VirementRegle"| NO["Notifications"]
    CO2 -->|"VirementRegle"| FI["Fidélité"]
```

Chaque flèche est un événement publié **sans savoir qui l'écoute**. Les démos 7 et 8 utilisaient déjà la chorégraphie,
mais seulement pour **notifier** (« un virement a eu lieu ») ; ici, elle **conduit** tout un processus métier, avec
attente humaine, compensations et pannes. C'est là qu'on voit ses forces et ses limites.

| | [Orchestration (démo 14)](14-saga-orchestree.md) | **Chorégraphie (cette démo)** |
|---|---|---|
| Le processus est écrit… | en un seul endroit (la définition de la saga) | **nulle part** : il émerge des abonnements |
| Un service connaît… | l'orchestrateur connaît tous les participants | **personne** ; seulement les contrats d'événements |
| Ajouter une réaction | modifier la définition | **s'abonner**, sans toucher aux autres |
| « Où en est le virement 42 ? » | l'état de la saga, stocké | une **projection** à construire (le Suivi) |
| Compensation | écrite à côté de chaque étape, déclenchée par le moteur | **dispersée** : chaque service sait quels événements défont son travail |
| Un participant en panne | le moteur réessaie, puis compense | **silence** : le processus reste en plan jusqu'à un délai d'expiration |
| Point unique de défaillance | l'orchestrateur | le courtier de messages |

---

## Les composants

- **Le courtier** ([Bus.cs](../21-Choregraphie/Choreo.Bus/Bus.cs), simulé dans le processus, sans package) : une file
  **par abonné** (un abonné lent ne retarde pas les autres), livraison « au moins une fois », nouveaux essais, lettres
  mortes, et le journal de tous les événements.
- **Les contrats** ([Contrats.cs](../21-Choregraphie/Choreo.Services/Contrats.cs)) : la forme des événements, seul
  code partagé. Chaque événement transporte **toutes les données du virement**, complétées au fil de l'eau (devises,
  montant en euros, puis montant converti) : chaque service y trouve ce qu'il lui faut sans rappeler personne.
- **Les services** ([Comptes.cs](../21-Choregraphie/Choreo.Services/Comptes.cs),
  [Autres.cs](../21-Choregraphie/Choreo.Services/Autres.cs)) : Comptes, Conformité, Change, Notifications, Fidélité,
  chacun dans son espace de noms, avec ses données. Un test d'architecture vérifie qu'aucun ne connaît les autres.
- **Le Suivi** ([Suivi.cs](../21-Choregraphie/Choreo.Services/Suivi.cs)) : il écoute tout et reconstitue l'état de
  chaque virement. Il observe, il ne décide rien.

Un service, c'est une liste d'abonnements. Voici tout ce que Comptes écoute :

```csharp
Bus.Abonner<VirementDemande>(Nom, Reserver);
Bus.Abonner<ConformiteRefusee>(Nom, e => Liberer(e, e.Motif));  // compensation
Bus.Abonner<ChangeEchoue>(Nom, e => Liberer(e, e.Motif));       // compensation
Bus.Abonner<DevisesConverties>(Nom, Regler);
```

En production : **RabbitMQ**, **Azure Service Bus** ou **Kafka**, avec **MassTransit**, **NServiceBus**, **Wolverine**
ou **Dapr** pour les abonnements, les nouveaux essais et les boîtes de réception.

---

## 1. Le processus n'existe que dans les réactions

Chaque événement porte la **corrélation** (le virement) et sa **cause** (l'événement auquel il répond) :

```csharp
/// <summary>Un nouvel événement, en réaction à celui-ci : même corrélation, et celui-ci pour cause.</summary>
public T Suite<T>(T suivant, string emetteur) where T : Evenement =>
    suivant with { Correlation = Correlation, Cause = Id, Emetteur = emetteur };
```

C'est le seul fil qui permet de raconter ce qui s'est passé. **Essai réel**, `GET /virements/{id}` :

```text
VirementDemande                                  ← API
  FondsReserves                                  ← Comptes, en réaction à VirementDemande
    ConformiteApprouvee                          ← Conformité
      DevisesConverties                          ← Change
        VirementRegle                            ← Comptes
          ClientNotifie                          ← Notifications : « Virement de 100,00 EUR effectué (108,32 USD reçus). »
```

Pour savoir **où en est** un virement, il faut un service de plus, le **Suivi**, qui écoute tous les événements (une
projection, comme en [CQRS](09-cqrs.md)). Son revers : il doit connaître chaque type d'événement. Ajouter une étape au
processus, c'est aussi penser à lui.

La seule « carte » du système est la liste des abonnements, et elle n'existe qu'à l'exécution (`GET /bus`) :

```text
Comptes        écoute ChangeEchoue, ConformiteRefusee, DevisesConverties, VirementDemande
Conformité     écoute FondsReserves, ReservationExpiree
Change         écoute ConformiteApprouvee
Notifications  écoute VirementRegle
Fidélité       écoute VirementRegle
Suivi          écoute les 13 types d'événements
```

---

## 2. Des compensations dispersées

Pas de moteur qui défait les étapes en ordre inverse : chaque service doit savoir **quels événements des autres
signifient « défais ce que tu as fait »**. Comptes libère la réservation sur `ConformiteRefusee` **et** sur
`ChangeEchoue` ; si demain un nouveau service peut échouer, il faudra penser à l'abonner aussi. Essai réel :

```text
Alice → Mallory (liste noire)    VirementDemande → FondsReserves → ConformiteRefusee → FondsLiberes      Annule
Alice → Yuki (yens, non cotés)   … → ConformiteApprouvee → ChangeEchoue → FondsLiberes                   Annule
Bob → Alice, 900 € (solde 500)   VirementDemande → FondsRefuses                                          Refuse
```

Comme dans la démo 14, les fonds sont d'abord **réservés** (indisponibles pour un autre virement, test
`L_argent_reserve_n_est_plus_disponible_pour_un_autre_virement`), et seulement débités au règlement : le point de
non-retour. Après lui (notification), on réessaie, on ne défait rien.

---

## 3. Quand un service se tait

Avec un orchestrateur, un participant en panne est réessayé puis compensé : quelqu'un attend sa réponse. Ici,
**personne n'attend personne**. Le change tombe en panne : le courtier réessaie 4 fois, met l'événement aux lettres
mortes… et c'est tout. Essai réel :

```text
POST /chaos { "changeEnPanne": true } ; POST /virements (200 €)   → statut EnCours, 200 € réservés sur le compte d'Alice
GET /bus/lettres-mortes          → Change, 4 tentatives, « Service de change indisponible. »
GET /virements/bloques           → le virement, sans activité récente
… 30 s plus tard                 → ReservationExpiree (émise par Comptes, sans cause) : Abandonne, 0 € réservé
```

Le filet de sécurité est **local** : Comptes se protège lui-même en donnant une durée de vie à ses réservations.

```csharp
expirees = _reservations.Where(r => r.Value.ExpireLe <= maintenant).ToList()
```

Et les autres services doivent en tenir compte : la Conformité écoute `ReservationExpiree` pour ne plus faire décider
un analyste sur un virement abandonné ; Comptes refuse un règlement arrivé trop tard (`VirementAbandonne`) ; le Suivi
ne rouvre pas un virement terminé. Chacun de ces cas est un **test**, et chacun a demandé une ligne de code à un
service différent : c'est le coût caché de la chorégraphie.

---

## 4. Livré deux fois, traité une fois

Les vrais courtiers livrent « au moins une fois » : après une coupure, un message peut arriver deux fois. Chaque service
garde donc les identifiants des événements déjà traités (sa « boîte de réception »), et change son état **et** publie
la suite d'un seul tenant :

```csharp
lock (Verrou)
{
    if (_traites.Contains(recu.Id))
        return;
    if (reagir() is { } suite)
        Bus.Publier(suite);
    _traites.Add(recu.Id);
}
```

Test `Des_evenements_livres_deux_fois_ne_produisent_leur_effet_qu_une_fois` : avec un courtier qui livre **chaque**
événement deux fois, 20 virements de 10 € donnent exactement 200 € débités, 20 notifications, 20 points de fidélité.
(Dans un vrai service avec une base, « d'un seul tenant » demande l'outbox de la [démo 8](08-microservices.md).)

---

## 5. Ajouter un participant sans toucher aux autres

Le service Fidélité (des points sur chaque virement réglé) a été ajouté **après coup**. Il s'abonne, c'est tout :

```csharp
public void Inscrire() => Bus.Abonner<VirementRegle>(Nom, e => UneSeuleFois(e, () =>
```

Aucun autre service n'a changé, aucun ne sait qu'il existe : le test `Ajouter_un_abonne_ne_change_rien_pour_les_autres`
fait tourner le système avec et sans lui, et obtient exactement les mêmes événements. C'est l'atout majeur de la
chorégraphie, et la raison pour laquelle elle convient si bien aux **réactions** (notifier, compter, archiver,
alimenter un tableau de bord) plutôt qu'aux processus.

---

## Les tests

Les services et le bus sans HTTP, avec une horloge manuelle pour l'expiration
([ChoregraphieTests.cs](../21-Choregraphie/Choreo.Tests/ChoregraphieTests.cs)), et l'API
([ApiTests.cs](../21-Choregraphie/Choreo.Tests/ApiTests.cs)).

| Test | Ce qui est vérifié |
|---|---|
| `Un_virement_avance_par_reactions_en_chaine_sans_que_personne_le_conduise` | les 6 événements, leurs émetteurs, la chaîne des causes |
| `Un_refus_de_la_conformite_est_compense_par_le_service_comptes`, `Un_echec_du_change_est_compense_lui_aussi` | compensations |
| `Un_solde_insuffisant_arrete_tout_des_la_premiere_reaction`, `L_argent_reserve_n_est_plus_disponible_pour_un_autre_virement` | réservation |
| `Un_gros_virement_attend_la_decision_d_un_analyste` | attente humaine, approbation et refus |
| `Un_service_en_panne_bloque_le_processus_jusqu_a_l_expiration_de_la_reservation` | lettres mortes, virement bloqué, expiration |
| `Apres_l_expiration_ni_l_analyste_ni_un_message_en_retard_ne_reglent_le_virement` | les cas tardifs, dans trois services |
| `Des_evenements_livres_deux_fois_ne_produisent_leur_effet_qu_une_fois` | boîte de réception |
| `Apres_le_point_de_non_retour_une_notification_en_echec_est_reessayee_jamais_compensee` | nouveaux essais, lettres mortes |
| `Ajouter_un_abonne_ne_change_rien_pour_les_autres` | extensibilité |
| `Aucun_service_ne_connait_les_autres` | **test d'architecture** (réflexion) |
| `Demander_un_virement_repond_202_et_la_suite_se_fait_sans_l_appelant`, `L_analyste_decide_par_l_API`, `Un_service_en_panne_se_voit_aux_lettres_mortes_et_aux_virements_bloques`, `Le_bus_montre_qui_ecoute_quoi` | l'API |

Vérifiés par mutation : pas de boîte de réception, chacune des deux compensations, expiration qui ne rend pas l'argent
ou n'a pas lieu, conformité sourde à l'expiration, règlement tardif silencieux, Suivi qui rouvre un virement terminé,
courtier sans nouvel essai, un service qui en référence un autre, réservation qui ignore l'argent déjà réservé : chacun
fait échouer au moins un test.

---

## Bilan

| 👍 Points forts | 👎 Limites |
|---|---|
| **Couplage minimal** : les services ne se connaissent pas | Le processus n'est écrit nulle part : difficile à comprendre, à modifier |
| **Ajouter une réaction** = s'abonner, sans toucher à rien | Compensations et délais **dispersés** dans les services |
| Pas de chef d'orchestre à exploiter ni à rendre disponible | Une panne se traduit par du **silence** : il faut surveiller (lettres mortes, virements bloqués) |
| Chaque service avance à son rythme, indépendamment | Suivre un processus demande une projection, et la traçabilité des causes |

**Quand l'utiliser** : pour les **réactions** à un fait (notifier, facturer, archiver, alimenter un entrepôt de données,
synchroniser un autre système), et les processus courts, stables, à peu de participants. Dès qu'un processus a de
nombreuses étapes, des attentes, des délais et des règles d'échec, l'[orchestration](14-saga-orchestree.md) est plus
sûre. Les deux se combinent souvent : un orchestrateur conduit le processus, et publie des événements que d'autres
services, eux, chorégraphient.

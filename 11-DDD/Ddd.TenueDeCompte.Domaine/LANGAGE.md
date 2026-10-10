# Langage omniprésent — contexte « Tenue de compte »

Le vocabulaire partagé entre les experts métier et les développeurs. Chaque terme existe tel quel dans le code :
si un expert dit « plafond de retrait journalier », on trouve `PlafondRetraitJournalier`, pas `MaxDailyLimit`.

| Terme | Définition | Dans le code |
|---|---|---|
| **Compte** | Compte de dépôt tenu par la banque, identifié par un IBAN, libellé dans une seule devise. | `Compte` (agrégat) |
| **Titulaire** | Personne au nom de laquelle le compte est ouvert. | `NomTitulaire` (objet-valeur) |
| **IBAN** | Identifiant international du compte, avec une clé de contrôle (modulo 97). | `Iban` (objet-valeur) |
| **Argent** | Un montant **et** sa devise. 10 € et 10 $ ne sont pas la même chose ; on ne les additionne pas. | `Argent` (objet-valeur) |
| **Devise** | Monnaie de tenue du compte : EUR, USD, CHF. | `Devise` (objet-valeur) |
| **Solde** | Argent disponible sur le compte. Jamais négatif (pas de découvert). | `Compte.Solde` |
| **Mouvement** | Toute écriture sur le compte : dépôt, retrait, virement émis ou reçu. | `Mouvement` (entité de l'agrégat) |
| **Retrait** | Sortie d'argent au guichet ou au distributeur. Soumis au plafond journalier. | `Compte.Retirer` |
| **Plafond de retrait journalier** | Total des retraits autorisé sur une même journée. Les virements n'y sont pas soumis. | `Compte.PlafondRetraitJournalier` |
| **Virement** | Transfert d'un compte de la banque vers un autre, désigné par son IBAN. Exprimé dans la devise du compte débité ; converti si le compte crédité est dans une autre devise. | `ServiceDeVirement` (service de domaine) |
| **Blocage** | Un compte bloqué ne peut plus être débité. Il peut encore recevoir des virements. | `Compte.Bloquer` |

Un même mot peut avoir un autre sens dans un autre contexte : pour la **Conformité**, un compte est un
**dossier de vigilance**, qui ne connaît ni solde ni plafond (voir `Ddd.Conformite/LANGAGE.md`).

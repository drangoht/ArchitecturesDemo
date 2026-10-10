# Langage omniprésent — contexte « Conformité »

Pour l'équipe Conformité, un compte n'a ni solde ni plafond : c'est une **source de flux à surveiller**.
Même réalité, autre modèle, autres mots. C'est pour cela que c'est un contexte délimité distinct.

| Terme | Définition | Dans le code |
|---|---|---|
| **Dossier de vigilance** | Ce que la Conformité sait d'un compte : les virements observés et les alertes levées. | `DossierDeVigilance` (agrégat) |
| **Virement observé** | Un virement vu par la Conformité : une date, un montant **en euros**, un bénéficiaire. La devise d'origine ne l'intéresse pas. | `VirementObserve` (objet-valeur) |
| **Seuil de déclaration** | Montant (10 000 €) à partir duquel un virement doit être signalé. | `DossierDeVigilance.SeuilDeDeclaration` |
| **Fractionnement** | Découper une grosse somme en plusieurs petits virements rapprochés pour passer sous les radars. Soupçonné à partir de 3 virements en 10 minutes. | `DossierDeVigilance.FenetreDeFractionnement` |
| **Alerte** | Signalement d'un comportement à examiner : *élevée* (à analyser) ou *critique* (blocage préventif demandé). | `Alerte` |
| **Blocage préventif** | Demande faite à la Tenue de compte de bloquer un compte en attendant l'analyse. | via `ITenueDeCompteApi` |

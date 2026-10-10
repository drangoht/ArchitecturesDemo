using System.Globalization;
using System.Text.Json;
using Sls.Plateforme;

namespace Sls.Fonctions;

// Chaque fonction est une méthode statique : entrée = le déclencheur + le contexte, sortie = une réponse (ou rien).
// Aucun état en mémoire entre deux appels : tout passe par les services gérés (tables, files, stockage).

public static class ComptesFonctions
{
    public record OuvertureDemande(string Titulaire, decimal DepotInitial);
    public record RetraitDemande(decimal Montant);

    public static async Task<ReponseHttp> Ouvrir(RequeteHttp requete, ContexteFonction contexte)
    {
        var demande = requete.Lire<OuvertureDemande>();
        if (string.IsNullOrWhiteSpace(demande.Titulaire) || demande.DepotInitial < 10)
            return ReponseHttp.Invalide("Titulaire obligatoire et dépôt initial d'au moins 10 €.");

        var compte = new Compte(Guid.NewGuid(), demande.Titulaire.Trim(), demande.DepotInitial, []);
        await contexte.Table(Tables.Comptes).Ecrire(compte.Id.ToString(), compte, etagAttendu: 0);
        contexte.Journaliser($"compte ouvert pour {compte.Titulaire}");
        return ReponseHttp.Cree($"/comptes/{compte.Id}", compte);
    }

    public static async Task<ReponseHttp> Consulter(RequeteHttp requete, ContexteFonction contexte)
    {
        var (compte, _) = await contexte.Table(Tables.Comptes).Lire<Compte>(requete.Parametres["id"]);
        return compte is null ? ReponseHttp.Introuvable("Compte introuvable.") : ReponseHttp.Ok(compte);
    }

    public static async Task<ReponseHttp> Retirer(RequeteHttp requete, ContexteFonction contexte)
    {
        var montant = requete.Lire<RetraitDemande>().Montant;
        if (montant <= 0)
            return ReponseHttp.Invalide("Le montant doit être strictement positif.");
        try
        {
            var compte = await Concurrence.Modifier<Compte>(contexte.Table(Tables.Comptes), requete.Parametres["id"],
                c => c.Appliquer(Guid.NewGuid(), -montant));
            return ReponseHttp.Ok(compte);
        }
        catch (KeyNotFoundException)
        {
            return ReponseHttp.Introuvable("Compte introuvable.");
        }
        catch (RegleMetierException e)
        {
            return ReponseHttp.Invalide(e.Message);
        }
    }
}

public static class VirementsFonctions
{
    public record VirementDemande(Guid Source, Guid Cible, decimal Montant);
    public record MessageVirement(Guid VirementId);

    /// <summary>HTTP : on ne fait PAS le virement ici. On l'accepte (202) et on le confie à une file : réponse immédiate.</summary>
    public static async Task<ReponseHttp> Demander(RequeteHttp requete, ContexteFonction contexte)
    {
        var demande = requete.Lire<VirementDemande>();
        if (await Valider(demande, contexte) is { } erreur)
            return ReponseHttp.Invalide(erreur);

        var virement = await Planifier(demande, contexte, Guid.NewGuid());
        return ReponseHttp.Accepte($"/virements/{virement.Id}", new { virementId = virement.Id, statut = virement.Statut });
    }

    public static async Task<ReponseHttp> Suivre(RequeteHttp requete, ContexteFonction contexte)
    {
        var (virement, _) = await contexte.Table(Tables.Virements).Lire<Virement>(requete.Parametres["id"]);
        return virement is null ? ReponseHttp.Introuvable("Virement introuvable.") : ReponseHttp.Ok(virement);
    }

    internal static async Task<string?> Valider(VirementDemande demande, ContexteFonction contexte)
    {
        if (demande.Montant <= 0)
            return "Le montant doit être strictement positif.";
        if (demande.Source == demande.Cible)
            return "Impossible de virer vers le même compte.";
        var comptes = contexte.Table(Tables.Comptes);
        if ((await comptes.Lire<Compte>(demande.Source.ToString())).Valeur is null
            || (await comptes.Lire<Compte>(demande.Cible.ToString())).Valeur is null)
            return "Compte source ou cible introuvable.";
        return null;
    }

    /// <summary>Enregistre le virement « en attente » et l'envoie dans la file. Rejouable : un identifiant déjà connu n'est pas recréé.</summary>
    internal static async Task<Virement> Planifier(VirementDemande demande, ContexteFonction contexte, Guid id)
    {
        var virements = contexte.Table(Tables.Virements);
        var virement = new Virement(id, demande.Source, demande.Cible, demande.Montant, "EnAttente");
        try
        {
            await virements.Ecrire(id.ToString(), virement, etagAttendu: 0);
        }
        catch (ConflitEcritureException)
        {
            virement = (await virements.Lire<Virement>(id.ToString())).Valeur!;
            if (virement.Statut != "EnAttente")
                return virement;  // déjà traité : rien à refaire
        }
        // Dans le doute, on renvoie le message : son exécution est idempotente.
        await contexte.EnvoyerDansFile(Files.Virements, new MessageVirement(id));
        return virement;
    }

    /// <summary>
    /// File « virements ». Peut être exécutée plusieurs fois pour le même message (nouvel essai, double livraison) :
    /// chaque étape est idempotente (l'identifiant du virement sert d'identifiant d'opération sur les comptes).
    /// Une exception = nouvel essai automatique ; un refus métier, lui, est définitif.
    /// </summary>
    public static async Task Executer(MessageFile message, ContexteFonction contexte)
    {
        var id = message.Lire<MessageVirement>().VirementId;
        var (virements, comptes) = (contexte.Table(Tables.Virements), contexte.Table(Tables.Comptes));

        var (virement, _) = await virements.Lire<Virement>(id.ToString());
        if (virement is null || virement.Statut != "EnAttente")
            return;

        Compte source;
        try
        {
            source = await Concurrence.Modifier<Compte>(comptes, virement.Source.ToString(), c => c.Appliquer(id, -virement.Montant));
        }
        catch (RegleMetierException e)
        {
            await virements.Ecrire(id.ToString(), virement with { Statut = "Refuse", Erreur = e.Message });
            contexte.Journaliser($"virement {id} refusé : {e.Message}");
            return;
        }

        await Task.Delay(200, contexte.Annulation);  // simulation : appel au système de compensation interbancaire

        var cible = await Concurrence.Modifier<Compte>(comptes, virement.Cible.ToString(), c => c.Appliquer(id, virement.Montant));

        var montant = virement.Montant.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
        await contexte.EnvoyerDansFile(Files.Notifications,
            new NotificationsFonctions.MessageNotification(source.Id, $"virement-{id}", $"Virement de {montant} € envoyé à {cible.Titulaire}."));
        await contexte.EnvoyerDansFile(Files.Notifications,
            new NotificationsFonctions.MessageNotification(cible.Id, $"virement-{id}", $"Virement de {montant} € reçu de {source.Titulaire}."));

        await virements.Ecrire(id.ToString(), virement with { Statut = "Effectue" });
        contexte.Journaliser($"virement {id} effectué ({montant} €)");
    }
}

public static class NotificationsFonctions
{
    public record MessageNotification(Guid CompteId, string Cle, string Texte);

    /// <summary>File « notifications ». Si le fournisseur de SMS est en panne, l'exception provoque de nouveaux essais.</summary>
    public static async Task Envoyer(MessageFile message, ContexteFonction contexte)
    {
        if (contexte.Reglage("Sms:EnPanne") == "true")
            throw new InvalidOperationException("Le fournisseur de SMS ne répond pas.");

        var notification = message.Lire<MessageNotification>();
        // Clé déterministe : une même notification rejouée écrase la précédente au lieu de créer un doublon.
        await contexte.Table(Tables.Notifications).Ecrire($"{notification.CompteId}/{notification.Cle}",
            new Notification(notification.CompteId, notification.Texte, DateTime.UtcNow));
        contexte.Journaliser($"SMS envoyé : {notification.Texte}");
    }

    public static async Task<ReponseHttp> Lister(RequeteHttp requete, ContexteFonction contexte) =>
        ReponseHttp.Ok(await contexte.Table(Tables.Notifications).Lister<Notification>($"{requete.Parametres["id"]}/"));
}

public static class ImportsFonctions
{
    /// <summary>HTTP : dépose le fichier CSV dans le stockage. C'est l'arrivée du fichier qui déclenche son traitement.</summary>
    public static async Task<ReponseHttp> Deposer(RequeteHttp requete, ContexteFonction contexte)
    {
        var chemin = $"imports/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv";
        await contexte.EcrireObjet(chemin, requete.Corps);
        return ReponseHttp.Accepte($"/plateforme/stockage/contenu?chemin={chemin}", new { fichier = chemin });
    }

    /// <summary>
    /// Stockage « imports/ » : une ligne = un message dans la file des virements (« fan-out »).
    /// La plateforme lance alors autant d'instances d'ExecuterVirement que nécessaire, en parallèle.
    /// </summary>
    public static async Task Traiter(ObjetStocke fichier, ContexteFonction contexte)
    {
        var lignes = fichier.Contenu.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var (planifies, rejetes) = (0, 0);
        for (var numero = 0; numero < lignes.Length; numero++)
        {
            var champs = lignes[numero].Split(';');
            if (champs.Length != 3 || !Guid.TryParse(champs[0], out var source) || !Guid.TryParse(champs[1], out var cible)
                || !decimal.TryParse(champs[2], CultureInfo.InvariantCulture, out var montant))
                continue;  // en-tête ou ligne illisible

            var demande = new VirementsFonctions.VirementDemande(source, cible, montant);
            if (await VirementsFonctions.Valider(demande, contexte) is { } erreur)
            {
                contexte.Journaliser($"ligne {numero + 1} rejetée : {erreur}");
                rejetes++;
                continue;
            }
            // Identifiant tiré du fichier et du numéro de ligne : retraiter le fichier ne crée aucun doublon.
            await VirementsFonctions.Planifier(demande, contexte, Concurrence.IdentifiantDeterministe($"{fichier.Chemin}#{numero}"));
            planifies++;
        }
        contexte.Journaliser($"{fichier.Chemin} : {planifies} virements planifiés, {rejetes} lignes rejetées");
    }
}

public static class RapportsFonctions
{
    /// <summary>Minuterie : produit un rapport dans le stockage, sans aucun serveur allumé entre deux exécutions.</summary>
    public static async Task Produire(TicMinuterie tic, ContexteFonction contexte)
    {
        var comptes = await contexte.Table(Tables.Comptes).Lister<Compte>();
        var virements = await contexte.Table(Tables.Virements).Lister<Virement>();
        var rapport = new
        {
            heure = tic.Heure,
            nombreComptes = comptes.Count,
            encoursTotal = comptes.Sum(c => c.Solde),
            virements = virements.GroupBy(v => v.Statut).ToDictionary(g => g.Key, g => g.Count())
        };
        await contexte.EcrireObjet($"rapports/{tic.Heure:yyyyMMdd-HHmmss}.json", JsonSerializer.Serialize(rapport));
    }
}

public static class VisitesFonctions
{
    /// <summary>Démontre la différence entre la mémoire d'une instance (trompeuse) et un service géré (fiable).</summary>
    public static async Task<ReponseHttp> Compter(RequeteHttp requete, ContexteFonction contexte)
    {
        // ✗ Mémoire de l'instance : propre à CETTE instance, remise à zéro à chaque démarrage à froid.
        var compteurDeLInstance = (contexte.MemoireInstance.TryGetValue("visites", out var v) ? (long)v : 0) + 1;
        contexte.MemoireInstance["visites"] = compteurDeLInstance;

        // ✓ Service géré : partagé par toutes les instances, durable.
        var global = await Concurrence.ModifierOuCreer(contexte.Table(Tables.Compteurs), "visites",
            new Compteur(0), c => c with { Valeur = c.Valeur + 1 });

        return ReponseHttp.Ok(new
        {
            instance = contexte.IdInstance,
            demarrageAFroid = contexte.DemarrageAFroid,
            compteurDeLInstance,
            compteurGlobal = global.Valeur
        });
    }
}

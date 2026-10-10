using Sls.Plateforme;

namespace Sls.Fonctions;

/// <summary>
/// Le manifeste de déploiement (l'équivalent de serverless.yml) : quelles fonctions, déclenchées par quoi,
/// avec combien de mémoire. Il n'y a ni serveur, ni contrôleur, ni Program.cs à écrire.
/// </summary>
public static class Deploiement
{
    public static void Declarer(Manifeste manifeste) => manifeste
        // Déclencheurs HTTP (derrière une passerelle API gérée)
        .Http("OuvrirCompte", "POST", "/comptes", ComptesFonctions.Ouvrir)
        .Http("ConsulterCompte", "GET", "/comptes/{id}", ComptesFonctions.Consulter)
        .Http("Retirer", "POST", "/comptes/{id}/retrait", ComptesFonctions.Retirer)
        .Http("DemanderVirement", "POST", "/virements", VirementsFonctions.Demander)
        .Http("SuivreVirement", "GET", "/virements/{id}", VirementsFonctions.Suivre)
        .Http("ListerNotifications", "GET", "/comptes/{id}/notifications", NotificationsFonctions.Lister)
        .Http("DeposerImport", "POST", "/imports", ImportsFonctions.Deposer)
        .Http("CompterVisites", "GET", "/visites", VisitesFonctions.Compter)

        // Déclencheurs de file : traitement asynchrone, avec nouveaux essais automatiques
        .File("ExecuterVirement", Files.Virements, VirementsFonctions.Executer, memoireMo: 256)
        .File("EnvoyerNotification", Files.Notifications, NotificationsFonctions.Envoyer)

        // Déclencheur de stockage : un fichier déposé lance son traitement
        .Stockage("TraiterImport", "imports/", ImportsFonctions.Traiter, memoireMo: 512)

        // Déclencheur planifié
        .Minuterie("ProduireRapport", TimeSpan.FromSeconds(30), RapportsFonctions.Produire);
}

public static class Tables
{
    public const string Comptes = "comptes";
    public const string Virements = "virements";
    public const string Notifications = "notifications";
    public const string Compteurs = "compteurs";
}

public static class Files
{
    public const string Virements = "virements";
    public const string Notifications = "notifications";
}

using System.Text.RegularExpressions;

namespace Mt.Locataires;

/// <summary>Où sont rangées les données d'un locataire.</summary>
public enum Isolation
{
    /// <summary>Une base commune à plusieurs locataires ; chaque ligne porte l'identifiant de son locataire (modèle « pool »).</summary>
    BasePartagee,

    /// <summary>Une base réservée au locataire (modèle « silo ») : isolation physique, mais un coût par locataire.</summary>
    BaseDediee
}

/// <summary>La formule souscrite : elle fixe l'isolation des données et les quotas, pas les règles métier.</summary>
public enum Plan { Standard, Premium }

/// <summary>
/// Les règles métier propres à chaque banque cliente : le même code, des paramètres différents.
/// Elles relèvent du contrat avec le client, et non de sa formule.
/// </summary>
public sealed record ReglesLocataire(decimal PlafondVirement, decimal FraisVirement, decimal FraisMensuels, int ComptesMax);

/// <summary>Ce qu'un locataire a le droit de consommer des ressources communes (le « voisin bruyant » est bridé).</summary>
public sealed record Quotas(int RequetesParMinute);

/// <summary>Ce qu'on déclare pour inscrire un locataire (fichier de configuration ou back-office).</summary>
public sealed record DefinitionLocataire(string Id, string Nom, Plan Plan, ReglesLocataire Regles);

/// <summary>Un client de la plateforme : ici, une banque. Tous les locataires partagent le même code et le même déploiement.</summary>
public sealed record Locataire(string Id, string Nom, Plan Plan, Isolation Isolation, ReglesLocataire Regles, Quotas Quotas, bool Actif = true)
{
    /// <summary>
    /// L'identifiant sert de sous-domaine (alpha.localhost) et de nom de base dédiée : on le restreint à des minuscules,
    /// chiffres et tirets, ce qui évite aussi toute injection par ce biais.
    /// </summary>
    public static bool IdentifiantValide(string id) => Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{1,29}$");
}

/// <summary>Ce que chaque formule implique techniquement.</summary>
public static class Formules
{
    public static Isolation IsolationDe(Plan plan) => plan == Plan.Premium ? Isolation.BaseDediee : Isolation.BasePartagee;

    public static Quotas QuotasDe(Plan plan) => plan == Plan.Premium ? new Quotas(600) : new Quotas(60);

    public static Locataire Creer(DefinitionLocataire d) =>
        new(d.Id, d.Nom, d.Plan, IsolationDe(d.Plan), d.Regles, QuotasDe(d.Plan));

    /// <summary>Même locataire, autre formule : l'isolation et les quotas suivent.</summary>
    public static Locataire Changer(Locataire l, Plan plan) =>
        l with { Plan = plan, Isolation = IsolationDe(plan), Quotas = QuotasDe(plan) };
}

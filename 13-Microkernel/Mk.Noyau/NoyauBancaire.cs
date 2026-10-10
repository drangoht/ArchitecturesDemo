using Mk.Contrats;

namespace Mk.Noyau;

public record Resultat(bool Succes, string? Erreur = null, decimal Frais = 0, decimal Solde = 0, bool Introuvable = false)
{
    public static Resultat Refus(string erreur) => new(false, erreur);
    public static Resultat CompteIntrouvable(Guid id) => new(false, $"Compte {id} introuvable.", Introuvable: true);
}

public record Export(string Contenu, string TypeMime);

/// <summary>
/// Le noyau : il tient les comptes et exécute les opérations, et rien d'autre.
/// Tout ce qui varie (contrôles, frais, formats de relevé) est délégué aux plugins, aux endroits prévus.
/// </summary>
public sealed class NoyauBancaire(RegistrePlugins plugins, TimeProvider horloge, Action<string> journal)
{
    private sealed class Compte(Guid id, string titulaire, decimal solde)
    {
        public Guid Id { get; } = id;
        public string Titulaire { get; } = titulaire;
        public decimal Solde { get; private set; } = solde;
        public List<LigneReleve> Lignes { get; } = [];

        public void Mouvement(decimal montant, string libelle, DateTime date)
        {
            Solde += montant;
            Lignes.Add(new LigneReleve(date, libelle, montant, Solde));
        }
    }

    private readonly Dictionary<Guid, Compte> _comptes = [];
    private readonly Lock _verrou = new();

    public void Ouvrir(Guid id, string titulaire, decimal depotInitial)
    {
        lock (_verrou)
        {
            var compte = new Compte(id, titulaire, 0);
            compte.Mouvement(depotInitial, "Dépôt initial", horloge.GetLocalNow().DateTime);
            _comptes[id] = compte;
        }
    }

    public ReleveCompte? Consulter(Guid id)
    {
        lock (_verrou)
            return _comptes.TryGetValue(id, out var c) ? new ReleveCompte(c.Id, c.Titulaire, c.Solde, c.Lignes.ToList()) : null;
    }

    public Resultat Retirer(Guid compteId, decimal montant) => Executer(TypeOperation.Retrait, compteId, null, montant);

    public Resultat Virer(Guid sourceId, Guid cibleId, decimal montant) => Executer(TypeOperation.Virement, sourceId, cibleId, montant);

    /// <summary>Relevé dans un format fourni par un plugin. Sans plugin de format, le noyau ne sait pas exporter.</summary>
    public Export? Exporter(Guid compteId, string format, out IReadOnlyList<string> formatsDisponibles)
    {
        var formats = plugins.Actifs<IFormatReleve>().ToList();
        formatsDisponibles = formats.Select(f => f.Format).ToList();

        var producteur = formats.FirstOrDefault(f => f.Format.Equals(format, StringComparison.OrdinalIgnoreCase));
        if (producteur is null || Consulter(compteId) is not { } releve)
            return null;

        string? contenu = null;
        return Proteger(producteur, () => contenu = producteur.Produire(releve)) ? new Export(contenu!, producteur.TypeMime) : null;
    }

    private Resultat Executer(TypeOperation type, Guid compteId, Guid? cibleId, decimal montant)
    {
        lock (_verrou)
        {
            if (montant <= 0)
                return Resultat.Refus("Le montant doit être strictement positif.");
            if (!_comptes.TryGetValue(compteId, out var compte))
                return Resultat.CompteIntrouvable(compteId);
            Compte? cible = null;
            if (cibleId is { } idCible && (!_comptes.TryGetValue(idCible, out cible) || idCible == compteId))
                return cible is null ? Resultat.CompteIntrouvable(idCible) : Resultat.Refus("Impossible de virer vers le même compte.");

            var date = horloge.GetLocalNow().DateTime;
            var operation = new OperationProposee(type, compte.Id, compte.Titulaire, montant, compte.Solde, cibleId, date);

            // 1. Point d'extension « contrôle » : un seul refus suffit.
            foreach (var controle in plugins.Actifs<IControleOperation>())
            {
                string? refus = null;
                if (!Proteger(controle, () => refus = controle.Verifier(operation)))
                    return Resultat.Refus($"Le plugin {controle.Nom} est en erreur : opération refusée par sécurité.");
                if (refus is not null)
                    return Resultat.Refus($"[{controle.Nom}] {refus}");
            }

            // 2. Point d'extension « frais » : chaque règle peut ajouter les siens.
            var frais = new List<(string Libelle, decimal Montant)>();
            foreach (var regle in plugins.Actifs<IRegleFrais>())
            {
                decimal montantFrais = 0;
                if (!Proteger(regle, () => montantFrais = regle.CalculerFrais(operation)))
                    return Resultat.Refus($"Le plugin {regle.Nom} est en erreur : opération refusée par sécurité.");
                if (montantFrais > 0)
                    frais.Add((regle.Libelle, montantFrais));
            }

            // 3. Le cœur du métier, qui n'appartient qu'au noyau.
            var totalFrais = frais.Sum(f => f.Montant);
            if (montant + totalFrais > compte.Solde)
                return Resultat.Refus($"Solde insuffisant (frais compris : {totalFrais:0.00} €).");

            compte.Mouvement(-montant, cible is null ? "Retrait" : $"Virement vers {cible.Titulaire}", date);
            foreach (var (libelle, montantFrais) in frais)
                compte.Mouvement(-montantFrais, libelle, date);
            cible?.Mouvement(montant, $"Virement de {compte.Titulaire}", date);

            return new Resultat(true, Frais: totalFrais, Solde: compte.Solde);
        }
    }

    /// <summary>
    /// Un plugin ne doit jamais faire tomber le noyau : son exception est capturée, et le plugin est mis « en erreur »
    /// (donc désactivé) jusqu'à ce qu'on le réactive ou qu'on le recharge.
    /// </summary>
    private bool Proteger(IPlugin plugin, Action appel)
    {
        try
        {
            appel();
            return true;
        }
        catch (Exception e)
        {
            plugins.SignalerErreur(plugin, e);
            journal($"[{plugin.Nom}] exception, plugin désactivé : {e.Message}");
            return false;
        }
    }
}

namespace Cell.Cellule;

/// <summary>Repère de l'assemblage, pour les tests.</summary>
public sealed class Marqueur;

public sealed class RegleMetierException(string message) : Exception(message);

public sealed class IntrouvableException(string message) : Exception(message);

// Format d'échange d'un client complet : création, migration d'une cellule à l'autre.
public sealed record CompteExport(Guid Id, string Libelle, decimal Solde);
public sealed record MouvementExport(Guid CompteId, DateTimeOffset Date, string Libelle, decimal Montant);
public sealed record ClientExport(string Id, string Nom, List<CompteExport> Comptes, List<MouvementExport> Mouvements, List<Guid> OperationsTraitees);

/// <summary>
/// Les données DE CETTE CELLULE : seulement les clients qui lui sont attribués. En production, chaque cellule a sa propre
/// base (sa propre instance, son propre compte cloud parfois) : une base qui tombe ne touche qu'une cellule.
/// </summary>
public sealed class Magasin
{
    private sealed class Client(string id, string nom)
    {
        public string Id { get; } = id;
        public string Nom { get; } = nom;
        public Dictionary<Guid, (string Libelle, decimal Solde)> Comptes { get; } = [];
        public List<MouvementExport> Mouvements { get; } = [];

        /// <summary>Les identifiants d'opération déjà appliqués (idempotence) : ils suivent le client s'il change de cellule.</summary>
        public HashSet<Guid> OperationsTraitees { get; } = [];
    }

    private readonly Lock _verrou = new();
    private readonly Dictionary<string, Client> _clients = new(StringComparer.OrdinalIgnoreCase);

    public int NombreClients
    {
        get { lock (_verrou) return _clients.Count; }
    }

    public bool Heberge(string client)
    {
        lock (_verrou) return _clients.ContainsKey(client);
    }

    public void Importer(ClientExport export)
    {
        lock (_verrou)
        {
            if (_clients.ContainsKey(export.Id))
                throw new RegleMetierException($"Le client « {export.Id} » est déjà dans cette cellule.");
            var client = new Client(export.Id, export.Nom);
            foreach (var c in export.Comptes)
                client.Comptes[c.Id] = (c.Libelle, c.Solde);
            client.Mouvements.AddRange(export.Mouvements);
            client.OperationsTraitees.UnionWith(export.OperationsTraitees);
            _clients[export.Id] = client;
        }
    }

    public ClientExport Exporter(string clientId)
    {
        lock (_verrou)
        {
            var c = Trouver(clientId);
            return new ClientExport(c.Id, c.Nom,
                c.Comptes.Select(x => new CompteExport(x.Key, x.Value.Libelle, x.Value.Solde)).ToList(),
                [.. c.Mouvements], [.. c.OperationsTraitees]);
        }
    }

    public void Supprimer(string clientId)
    {
        lock (_verrou)
            _clients.Remove(clientId);
    }

    public IReadOnlyList<CompteExport> Comptes(string clientId)
    {
        lock (_verrou)
            return Trouver(clientId).Comptes.Select(x => new CompteExport(x.Key, x.Value.Libelle, x.Value.Solde)).ToList();
    }

    public IReadOnlyList<MouvementExport> Mouvements(string clientId, Guid compte)
    {
        lock (_verrou)
            return Trouver(clientId).Mouvements.Where(m => m.CompteId == compte).OrderByDescending(m => m.Date).ToList();
    }

    /// <summary>Modifie un solde. <paramref name="calcul"/> donne le nouveau solde : c'est là que la version du code intervient.</summary>
    public decimal Mouvementer(string clientId, Guid compte, Func<decimal, decimal> calcul, string libelle, DateTimeOffset date, Guid? operationId = null)
    {
        lock (_verrou)
        {
            var c = Trouver(clientId);
            if (!c.Comptes.TryGetValue(compte, out var actuel))
                throw new IntrouvableException($"Compte {compte} introuvable chez {clientId}.");
            if (operationId is { } id && !c.OperationsTraitees.Add(id))
                return actuel.Solde;   // déjà appliquée : un nouvel essai ne crédite pas deux fois
            var nouveau = calcul(actuel.Solde);
            if (nouveau < 0)
            {
                if (operationId is { } annule)
                    c.OperationsTraitees.Remove(annule);
                throw new RegleMetierException($"Solde insuffisant ({actuel.Solde:0.00} €).");
            }
            c.Comptes[compte] = (actuel.Libelle, nouveau);
            c.Mouvements.Add(new MouvementExport(compte, date, libelle, nouveau - actuel.Solde));
            return nouveau;
        }
    }

    /// <summary>Virement entre deux clients de LA MÊME cellule : une seule transaction locale, comme dans un monolithe.</summary>
    public decimal VirerLocalement(string source, Guid compteSource, string cible, Guid compteCible, decimal montant, DateTimeOffset date)
    {
        lock (_verrou)
        {
            var s = Trouver(source);
            var c = Trouver(cible);
            if (!s.Comptes.TryGetValue(compteSource, out var debit) || !c.Comptes.TryGetValue(compteCible, out var credit))
                throw new IntrouvableException("Compte introuvable.");
            if (debit.Solde < montant)
                throw new RegleMetierException($"Solde insuffisant ({debit.Solde:0.00} €).");
            s.Comptes[compteSource] = (debit.Libelle, debit.Solde - montant);
            c.Comptes[compteCible] = (credit.Libelle, credit.Solde + montant);
            s.Mouvements.Add(new MouvementExport(compteSource, date, $"Virement vers {c.Nom}", -montant));
            c.Mouvements.Add(new MouvementExport(compteCible, date, $"Virement de {s.Nom}", montant));
            return debit.Solde - montant;
        }
    }

    private Client Trouver(string clientId) =>
        _clients.GetValueOrDefault(clientId) ?? throw new IntrouvableException($"Le client « {clientId} » n'est pas dans cette cellule.");
}

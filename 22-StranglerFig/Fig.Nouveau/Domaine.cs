namespace Fig.Nouveau;

/// <summary>Repère de l'assemblage, pour les tests (chaque projet de la démo a son propre Program).</summary>
public sealed class Marqueur;

public enum Origine { Ancien, Nouveau }

/// <summary>Une opération, dans le modèle du nouveau service : euros en decimal, date avec son fuseau.</summary>
public sealed record Operation(string Id, DateTimeOffset Date, string Libelle, decimal Montant, Origine Origine);

public sealed class Compte(string numero, string titulaire)
{
    private readonly List<Operation> _operations = [];

    public string Numero { get; } = numero;
    public string Titulaire { get; } = titulaire;
    public decimal Solde { get; private set; }

    /// <summary>Dans l'ordre d'enregistrement.</summary>
    public IReadOnlyList<Operation> Operations => _operations;

    internal bool Enregistrer(Operation operation)
    {
        if (_operations.Any(o => o.Id == operation.Id))
            return false;   // déjà connue : un lot rejoué, ou l'écho d'une opération écrite ici, ne compte pas deux fois
        _operations.Add(operation);
        Solde += operation.Montant;
        return true;
    }
}

public sealed class ErreurMetier(int statut, string message) : Exception(message)
{
    public int Statut => statut;
}

// Ce que la couche anti-corruption extrait du journal de l'ancien, dans le vocabulaire du nouveau service.
public abstract record FaitAncien;
public sealed record CompteOuvert(string Numero, string Titulaire) : FaitAncien;
public sealed record OperationEnregistree(string Numero, Operation Operation) : FaitAncien;

/// <summary>Une opération écrite ici, à reporter dans l'ancien (boîte d'envoi, remplie dans le même verrou que l'écriture).</summary>
public sealed record OperationAReporter(string Numero, Operation Operation);

public sealed record DemandeVirement(string Source, string Cible, decimal Montant);
public sealed record ResultatVirement(string Reference, decimal SoldeSource);

/// <summary>Les données du nouveau service, avec la position atteinte dans le journal de l'ancien et la boîte d'envoi.</summary>
public sealed class Depot
{
    private readonly object _verrou = new();
    private readonly Dictionary<string, Compte> _comptes = [];
    private readonly List<OperationAReporter> _aReporter = [];
    private long _positionAncien;
    private long _numeroVirement;

    public long PositionAncien
    {
        get { lock (_verrou) return _positionAncien; }
    }

    public T Lire<T>(string numero, Func<Compte, T> lecture)
    {
        lock (_verrou)
            return lecture(_comptes.GetValueOrDefault(numero) ?? throw new ErreurMetier(404, "COMPTE INCONNU"));
    }

    /// <summary>Applique des faits venus de l'ancien ET avance la position, ensemble : un lot est appliqué en entier ou pas du tout.</summary>
    public int AppliquerDepuisAncien(IReadOnlyList<FaitAncien> faits, long position)
    {
        lock (_verrou)
        {
            var appliques = 0;
            foreach (var fait in faits)
            {
                switch (fait)
                {
                    case CompteOuvert o when !_comptes.ContainsKey(o.Numero):
                        _comptes[o.Numero] = new Compte(o.Numero, o.Titulaire);
                        appliques++;
                        break;
                    case OperationEnregistree e when _comptes[e.Numero].Enregistrer(e.Operation):
                        appliques++;
                        break;
                }
            }
            _positionAncien = Math.Max(_positionAncien, position);
            return appliques;
        }
    }

    /// <summary>
    /// Le virement, réécrit dans le nouveau service. Les règles et les messages d'erreur sont ceux de l'ancien : ils font
    /// partie du contrat. Les deux opérations et leur report vers l'ancien sont enregistrés ensemble (boîte d'envoi).
    /// </summary>
    public ResultatVirement Virer(string source, string cible, decimal montant, DateTimeOffset maintenant)
    {
        if (montant <= 0 || decimal.Round(montant, 2) != montant)
            throw new ErreurMetier(400, "MONTANT INVALIDE");
        if (source == cible)
            throw new ErreurMetier(400, "COMPTES IDENTIQUES");
        lock (_verrou)
        {
            var debite = _comptes.GetValueOrDefault(source) ?? throw new ErreurMetier(404, "COMPTE INCONNU");
            var credite = _comptes.GetValueOrDefault(cible) ?? throw new ErreurMetier(404, "COMPTE INCONNU");
            if (debite.Solde < montant)
                throw new ErreurMetier(400, "SOLDE INSUFFISANT");
            var reference = $"VIR-N{++_numeroVirement:D5}";
            var debit = new Operation($"N-{Guid.NewGuid():N}", maintenant, $"VIR VERS {cible}", -montant, Origine.Nouveau);
            var credit = new Operation($"N-{Guid.NewGuid():N}", maintenant, $"VIR DE {source}", montant, Origine.Nouveau);
            debite.Enregistrer(debit);
            credite.Enregistrer(credit);
            _aReporter.Add(new OperationAReporter(source, debit));
            _aReporter.Add(new OperationAReporter(cible, credit));
            return new ResultatVirement(reference, debite.Solde);
        }
    }

    public IReadOnlyList<OperationAReporter> AReporter()
    {
        lock (_verrou)
            return _aReporter.ToList();
    }

    public void MarquerReportees(IEnumerable<OperationAReporter> reportees)
    {
        lock (_verrou)
            foreach (var r in reportees)
                _aReporter.Remove(r);
    }

    public IReadOnlyList<string> Numeros()
    {
        lock (_verrou)
            return _comptes.Keys.Order().ToList();
    }
}

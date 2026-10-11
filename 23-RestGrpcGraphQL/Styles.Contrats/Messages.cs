using Styles.Outillage.Protobuf;

namespace Styles.Contrats;

// Les messages de banque.proto, tels que les générerait protoc (simplifiés). Un test vérifie la correspondance.

public static class ServiceBanque
{
    /// <summary>Le nom complet du service : « paquet.Service », la première partie du chemin HTTP de chaque méthode.</summary>
    public const string Nom = "banque.Banque";
}

public enum TypeCompte { TypeCompteInconnu = 0, Courant = 1, Epargne = 2 }

public sealed class DemandeTableauDeBord
{
    [Champ(1)] public string ClientId { get; set; } = "";
    [Champ(2)] public int OperationsParCompte { get; set; }
}

public sealed class TableauDeBord
{
    [Champ(1)] public string ClientId { get; set; } = "";
    [Champ(2)] public string Nom { get; set; } = "";
    [Champ(3)] public List<Compte> Comptes { get; set; } = [];
}

public sealed class DemandeCompte
{
    [Champ(1)] public string Numero { get; set; } = "";
}

public sealed class Compte
{
    [Champ(1)] public string Numero { get; set; } = "";
    [Champ(2)] public string Libelle { get; set; } = "";
    [Champ(3)] public TypeCompte Type { get; set; }
    [Champ(4)] public long SoldeCentimes { get; set; }
    [Champ(5)] public List<Operation> DernieresOperations { get; set; } = [];
}

public sealed class Operation
{
    [Champ(1)] public string Id { get; set; } = "";
    [Champ(2)] public long DateUnixMs { get; set; }
    [Champ(3)] public string Libelle { get; set; } = "";
    [Champ(4, ZigZag = true)] public long MontantCentimes { get; set; }
}

public sealed class DemandeVirement
{
    [Champ(1)] public string Source { get; set; } = "";
    [Champ(2)] public string Cible { get; set; } = "";
    [Champ(3)] public long MontantCentimes { get; set; }
    [Champ(4)] public string Libelle { get; set; } = "";
}

public sealed class Virement
{
    [Champ(1)] public string Id { get; set; } = "";
    [Champ(2)] public string Source { get; set; } = "";
    [Champ(3)] public string Cible { get; set; } = "";
    [Champ(4)] public long MontantCentimes { get; set; }
    [Champ(5)] public long SoldeSourceCentimes { get; set; }
}

public sealed class DemandeSuivi
{
    [Champ(1)] public string Numero { get; set; } = "";
}

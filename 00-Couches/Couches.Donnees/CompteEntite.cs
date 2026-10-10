namespace Couches.Donnees;

/// <summary>
/// Entité « anémique » : uniquement des données, avec des setters publics.
/// Les règles (solde suffisant…) sont dans la couche métier, pas ici.
/// </summary>
public class CompteEntite
{
    public Guid Id { get; set; }
    public string Titulaire { get; set; } = "";
    public decimal Solde { get; set; }
}

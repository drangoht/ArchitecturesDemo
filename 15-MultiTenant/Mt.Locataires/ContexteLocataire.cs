namespace Mt.Locataires;

/// <summary>
/// « Pour quel locataire suis-je en train de travailler ? » Une instance par requête (ou par tâche de fond).
/// Le métier et l'accès aux données ne lisent le locataire qu'ici, jamais dans la requête HTTP.
/// </summary>
public interface IContexteLocataire
{
    Locataire? Courant { get; }

    /// <summary>Le locataire courant, ou une exception : sans locataire, on refuse de travailler plutôt que de tout voir.</summary>
    Locataire Exiger();
}

public sealed class AucunLocataireException()
    : InvalidOperationException("Aucun locataire défini : accès aux données refusé.");

public sealed class ContexteLocataire : IContexteLocataire
{
    public Locataire? Courant { get; private set; }

    public static ContexteLocataire Pour(Locataire locataire)
    {
        var contexte = new ContexteLocataire();
        contexte.Definir(locataire);
        return contexte;
    }

    /// <summary>Fixé une fois, au début de la requête : changer de locataire en cours de route est refusé.</summary>
    public void Definir(Locataire locataire)
    {
        if (Courant is not null && !string.Equals(Courant.Id, locataire.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Le contexte appartient déjà au locataire « {Courant.Id} ».");
        Courant = locataire;
    }

    public Locataire Exiger() => Courant ?? throw new AucunLocataireException();
}

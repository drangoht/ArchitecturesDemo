namespace Clean.AdaptateursInterface.Presenters;

/// <summary>
/// View model « HTTP » : un code de statut et un corps prêt à sérialiser.
/// Le cercle des frameworks n'a plus qu'à l'écrire dans la réponse.
/// </summary>
public record VueHttp(int Statut, object Corps)
{
    public static VueHttp Ok(object corps) => new(200, corps);
    public static VueHttp Introuvable(Guid compteId) => new(404, new { erreur = $"Compte {compteId} introuvable." });
    public static VueHttp Refus(string raison) => new(400, new { erreur = raison });
}

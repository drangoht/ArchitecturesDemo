using System.Security.Cryptography;
using System.Text;
using Sls.Plateforme;

namespace Sls.Fonctions;

public class RegleMetierException(string message) : Exception(message);

/// <param name="Operations">
/// Dernières opérations appliquées : un mouvement rejoué (nouvel essai, double livraison) est reconnu et ignoré.
/// </param>
public record Compte(Guid Id, string Titulaire, decimal Solde, Guid[] Operations)
{
    public Compte Appliquer(Guid operation, decimal variation)
    {
        if (Operations.Contains(operation))
            return this;
        if (Solde + variation < 0)
            throw new RegleMetierException("Solde insuffisant.");
        return this with { Solde = Solde + variation, Operations = [.. Operations.TakeLast(49), operation] };
    }
}

public record Virement(Guid Id, Guid Source, Guid Cible, decimal Montant, string Statut, string? Erreur = null);

public record Notification(Guid CompteId, string Message, DateTime Date);

public record Compteur(long Valeur);

internal static class Concurrence
{
    /// <summary>
    /// Lire → modifier → écrire « seulement si personne n'a écrit entre-temps », et recommencer sinon.
    /// Indispensable : des dizaines d'instances de la même fonction peuvent toucher la même donnée en même temps.
    /// </summary>
    public static async Task<T> Modifier<T>(TableGeree table, string cle, Func<T, T> modification) where T : class
    {
        for (var essai = 1; ; essai++)
        {
            var (valeur, etag) = await table.Lire<T>(cle);
            if (valeur is null)
                throw new KeyNotFoundException($"{table.Nom}/{cle}");
            try
            {
                var nouvelle = modification(valeur);
                await table.Ecrire(cle, nouvelle, etag);
                return nouvelle;
            }
            catch (ConflitEcritureException) when (essai < 20)
            {
                // Une autre instance a écrit avant nous : on relit et on recommence.
            }
        }
    }

    public static async Task<T> ModifierOuCreer<T>(TableGeree table, string cle, T initiale, Func<T, T> modification) where T : class
    {
        try
        {
            await table.Ecrire(cle, initiale, etagAttendu: 0);
        }
        catch (ConflitEcritureException)
        {
            // Elle existait déjà.
        }
        return await Modifier(table, cle, modification);
    }

    /// <summary>Même entrée → même identifiant : permet de rejouer un traitement sans créer de doublons.</summary>
    public static Guid IdentifiantDeterministe(string texte) => new(MD5.HashData(Encoding.UTF8.GetBytes(texte)));
}

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static void Charger(ServicesGeres services)
    {
        var comptes = services.Table(Tables.Comptes);
        comptes.Ecrire(Alice.ToString(), new Compte(Alice, "Alice", 1_000m, []));
        comptes.Ecrire(Bob.ToString(), new Compte(Bob, "Bob", 500m, []));
        comptes.Ecrire(Carla.ToString(), new Compte(Carla, "Carla", 5_000m, []));
    }
}

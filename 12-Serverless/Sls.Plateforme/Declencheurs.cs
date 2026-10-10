using System.Text.Json;

namespace Sls.Plateforme;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

// ─── Ce que reçoit une fonction, selon ce qui l'a déclenchée ───

public record RequeteHttp(string Methode, string Chemin, IReadOnlyDictionary<string, string> Parametres, string Corps)
{
    public T Lire<T>() => JsonSerializer.Deserialize<T>(Corps, Json.Options) ?? throw new FormatException("Corps de requête vide.");
}

public record ReponseHttp(int Statut, object? Corps = null, string? Location = null)
{
    public static ReponseHttp Ok(object? corps) => new(200, corps);
    public static ReponseHttp Cree(string location, object corps) => new(201, corps, location);
    public static ReponseHttp Accepte(string location, object corps) => new(202, corps, location);
    public static ReponseHttp Invalide(string erreur) => new(400, new { erreur });
    public static ReponseHttp Introuvable(string erreur) => new(404, new { erreur });
}

/// <param name="Tentative">1 pour la première livraison, 2 pour le premier nouvel essai, etc.</param>
public record MessageFile(string Id, string File, string Corps, int Tentative)
{
    public T Lire<T>() => JsonSerializer.Deserialize<T>(Corps, Json.Options)!;
}

public record TicMinuterie(DateTime Heure);

public record ObjetStocke(string Chemin, string Contenu);

// ─── Erreurs de la plateforme ───

public class EtranglementException(string fonction)
    : Exception($"{fonction} : nombre maximal d'instances atteint, la requête est refusée (étranglement).");

public class DelaiDepasseException(string fonction, TimeSpan delai)
    : Exception($"{fonction} : délai d'exécution maximal de {delai.TotalSeconds:0.##} s dépassé, l'instance a été arrêtée.");

public class ConflitEcritureException(string table, string cle)
    : Exception($"Conflit d'écriture sur {table}/{cle} : l'entité a été modifiée entre-temps.");

using System.Buffers.Binary;
using System.Globalization;

namespace Styles.Outillage.Grpc;

/// <summary>Les codes de statut de gRPC : ils voyagent dans les « trailers » HTTP/2, pas dans le statut HTTP (toujours 200).</summary>
public enum StatutGrpc
{
    Ok = 0, Cancelled = 1, Unknown = 2, InvalidArgument = 3, DeadlineExceeded = 4, NotFound = 5, AlreadyExists = 6,
    PermissionDenied = 7, ResourceExhausted = 8, FailedPrecondition = 9, Aborted = 10, OutOfRange = 11,
    Unimplemented = 12, Internal = 13, Unavailable = 14, DataLoss = 15, Unauthenticated = 16
}

public sealed class ErreurGrpc(StatutGrpc statut, string message) : Exception(message)
{
    public StatutGrpc Statut => statut;
}

/// <summary>
/// Le format filaire de gRPC sur HTTP/2 : chaque message est précédé de 5 octets, un indicateur de compression (0 ici)
/// puis sa longueur sur 4 octets (gros-boutiste). Un flux, c'est simplement plusieurs trames à la suite.
/// </summary>
public static class TrameGrpc
{
    public const string TypeDeContenu = "application/grpc";

    public static byte[] Encadrer(byte[] message)
    {
        var trame = new byte[5 + message.Length];
        BinaryPrimitives.WriteUInt32BigEndian(trame.AsSpan(1), (uint)message.Length);
        message.CopyTo(trame, 5);
        return trame;
    }

    public static async Task Ecrire(Stream flux, byte[] message, CancellationToken annulation)
    {
        await flux.WriteAsync(Encadrer(message), annulation);
        await flux.FlushAsync(annulation);   // chaque message part tout de suite : c'est ce qui fait un flux
    }

    /// <summary>Le message suivant, ou null si le flux est terminé proprement.</summary>
    public static async Task<byte[]?> Lire(Stream flux, CancellationToken annulation)
    {
        var enTete = new byte[5];
        var lus = await flux.ReadAtLeastAsync(enTete, 5, throwOnEndOfStream: false, annulation);
        if (lus == 0)
            return null;
        if (lus < 5)
            throw new ErreurGrpc(StatutGrpc.Internal, "Trame gRPC tronquée.");
        if (enTete[0] != 0)
            throw new ErreurGrpc(StatutGrpc.Unimplemented, "Messages compressés non pris en charge.");
        var longueur = BinaryPrimitives.ReadUInt32BigEndian(enTete.AsSpan(1));
        if (longueur > 4 * 1024 * 1024)
            throw new ErreurGrpc(StatutGrpc.ResourceExhausted, "Message de plus de 4 Mo.");
        var message = new byte[longueur];
        await flux.ReadExactlyAsync(message, annulation);
        return message;
    }

    /// <summary>L'en-tête grpc-timeout : un entier suivi d'une unité (H, M, S, m pour milli, u pour micro, n pour nano).</summary>
    public static string FormaterDelai(TimeSpan delai) => $"{Math.Max(1, (long)Math.Ceiling(delai.TotalMilliseconds))}m";

    public static TimeSpan? LireDelai(string? valeur)
    {
        if (string.IsNullOrEmpty(valeur) || valeur.Length < 2 || !long.TryParse(valeur[..^1], CultureInfo.InvariantCulture, out var n))
            return null;
        return valeur[^1] switch
        {
            'H' => TimeSpan.FromHours(n),
            'M' => TimeSpan.FromMinutes(n),
            'S' => TimeSpan.FromSeconds(n),
            'm' => TimeSpan.FromMilliseconds(n),
            'u' => TimeSpan.FromMicroseconds(n),
            'n' => TimeSpan.FromTicks(n / 100),
            _ => null
        };
    }
}

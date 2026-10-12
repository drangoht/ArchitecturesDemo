using System.Buffers.Binary;
using System.Globalization;

namespace Styles.Tooling.Grpc;

/// <summary>The gRPC status codes: they travel in the HTTP/2 "trailers", not in the HTTP status (always 200).</summary>
public enum GrpcStatus
{
    Ok = 0, Cancelled = 1, Unknown = 2, InvalidArgument = 3, DeadlineExceeded = 4, NotFound = 5, AlreadyExists = 6,
    PermissionDenied = 7, ResourceExhausted = 8, FailedPrecondition = 9, Aborted = 10, OutOfRange = 11,
    Unimplemented = 12, Internal = 13, Unavailable = 14, DataLoss = 15, Unauthenticated = 16
}

public sealed class GrpcError(GrpcStatus status, string message) : Exception(message)
{
    public GrpcStatus Status => status;
}

/// <summary>
/// The gRPC wire format over HTTP/2: each message is preceded by 5 bytes, a compression flag (0 here)
/// then its length on 4 bytes (big-endian). A stream is simply several frames in a row.
/// </summary>
public static class GrpcFrame
{
    public const string ContentType = "application/grpc";

    public static byte[] Frame(byte[] message)
    {
        var frame = new byte[5 + message.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)message.Length);
        message.CopyTo(frame, 5);
        return frame;
    }

    public static async Task Write(Stream stream, byte[] message, CancellationToken cancellation)
    {
        await stream.WriteAsync(Frame(message), cancellation);
        await stream.FlushAsync(cancellation);   // each message leaves right away: that is what makes a stream
    }

    /// <summary>The next message, or null if the stream ended cleanly.</summary>
    public static async Task<byte[]?> Read(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[5];
        var read = await stream.ReadAtLeastAsync(header, 5, throwOnEndOfStream: false, cancellation);
        if (read == 0)
            return null;
        if (read < 5)
            throw new GrpcError(GrpcStatus.Internal, "Truncated gRPC frame.");
        if (header[0] != 0)
            throw new GrpcError(GrpcStatus.Unimplemented, "Compressed messages not supported.");
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
        if (length > 4 * 1024 * 1024)
            throw new GrpcError(GrpcStatus.ResourceExhausted, "Message larger than 4 MB.");
        var message = new byte[length];
        await stream.ReadExactlyAsync(message, cancellation);
        return message;
    }

    /// <summary>The grpc-timeout header: an integer followed by a unit (H, M, S, m for milli, u for micro, n for nano).</summary>
    public static string FormatTimeout(TimeSpan timeout) => $"{Math.Max(1, (long)Math.Ceiling(timeout.TotalMilliseconds))}m";

    public static TimeSpan? ReadTimeout(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2 || !long.TryParse(value[..^1], CultureInfo.InvariantCulture, out var n))
            return null;
        return value[^1] switch
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

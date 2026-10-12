using System.Text.Json;

namespace Sls.Platform;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

// ─── What a function receives, depending on what triggered it ───

public record HttpRequestData(string Method, string Path, IReadOnlyDictionary<string, string> Parameters, string Body)
{
    public T Read<T>() => JsonSerializer.Deserialize<T>(Body, Json.Options) ?? throw new FormatException("Empty request body.");
}

public record HttpResponseData(int Status, object? Body = null, string? Location = null)
{
    public static HttpResponseData Ok(object? body) => new(200, body);
    public static HttpResponseData Created(string location, object body) => new(201, body, location);
    public static HttpResponseData Accepted(string location, object body) => new(202, body, location);
    public static HttpResponseData Invalid(string error) => new(400, new { error });
    public static HttpResponseData NotFound(string error) => new(404, new { error });
}

/// <param name="Attempt">1 for the first delivery, 2 for the first retry, etc.</param>
public record QueueMessage(string Id, string Queue, string Body, int Attempt)
{
    public T Read<T>() => JsonSerializer.Deserialize<T>(Body, Json.Options)!;
}

public record TimerTick(DateTime Time);

public record StoredObject(string Path, string Content);

// ─── Platform errors ───

public class ThrottlingException(string function)
    : Exception($"{function}: maximum number of instances reached, the request is refused (throttling).");

public class TimeoutExceededException(string function, TimeSpan timeout)
    : Exception($"{function}: maximum execution time of {timeout.TotalSeconds:0.##} s exceeded, the instance was stopped.");

public class WriteConflictException(string table, string key)
    : Exception($"Write conflict on {table}/{key}: the entity was changed in the meantime.");

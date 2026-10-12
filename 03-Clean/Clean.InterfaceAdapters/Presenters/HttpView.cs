namespace Clean.InterfaceAdapters.Presenters;

/// <summary>
/// "HTTP" view model: a status code and a body ready to serialize.
/// The frameworks circle only has to write it into the response.
/// </summary>
public record HttpView(int Status, object Body)
{
    public static HttpView Ok(object body) => new(200, body);
    public static HttpView NotFound(Guid accountId) => new(404, new { error = $"Account {accountId} not found." });
    public static HttpView Refused(string reason) => new(400, new { error = reason });
}

namespace Layered.Data;

/// <summary>
/// "Anemic" entity: data only, with public setters.
/// The rules (sufficient balance…) live in the business layer, not here.
/// </summary>
public class AccountEntity
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public decimal Balance { get; set; }
}

using System.Globalization;
using Clean.UseCases.Lookup;

namespace Clean.InterfaceAdapters.Presenters;

/// <summary>
/// Second presenter for the SAME use case: a readable text statement.
/// That is the point of the output boundary: a format is added without touching the interactor.
/// </summary>
public class LookupTextPresenter : ILookupOutput
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    public int Status { get; private set; } = 200;
    public string Text { get; private set; } = "";

    public void Success(LookupResponse r) =>
        Text = $"""
            ACCOUNT STATEMENT
            Owner   : {r.Owner}
            Balance : {r.Balance.ToString("N2", English)} €
            """;

    public void AccountNotFound(Guid accountId)
    {
        Status = 404;
        Text = $"No account has the number {accountId}.";
    }
}

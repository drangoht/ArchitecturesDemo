using Clean.InterfaceAdapters.Presenters;
using Clean.UseCases.Lookup;
using Clean.UseCases.Withdrawal;

namespace Clean.Tests.Presenters;

// Presenters are tested alone, without an interactor: we give them a response and read the view.
public class PresentersTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void The_text_presenter_produces_a_readable_statement()
    {
        var presenter = new LookupTextPresenter();

        presenter.Success(new LookupResponse(Id, "Alice", 1234.5m));

        Assert.Equal(200, presenter.Status);
        Assert.Contains("Owner   : Alice", presenter.Text);
        Assert.Contains("1,234.50 €", presenter.Text);
    }

    [Fact]
    public void The_http_presenter_translates_a_refusal_into_400()
    {
        var presenter = new WithdrawalHttpPresenter();

        ((IWithdrawalOutput)presenter).Refused("Insufficient balance.");

        Assert.Equal(400, presenter.View.Status);
    }

    [Fact]
    public void The_http_presenter_translates_an_unknown_account_into_404()
    {
        var presenter = new LookupHttpPresenter();

        presenter.AccountNotFound(Id);

        Assert.Equal(404, presenter.View.Status);
    }
}

using Onion.Domain.Model;
using Onion.Domain.Services;

namespace Onion.Tests.Domain;

// The central layers are tested without any double: they are pure objects.
public class TransferServiceTests
{
    private readonly TransferService _service = new();
    private readonly Account _alice = new(Guid.NewGuid(), "Alice", 10_000m);
    private readonly Account _bob = new(Guid.NewGuid(), "Bob", 0m);

    [Fact]
    public void A_transfer_moves_the_money()
    {
        _service.Transfer(_alice, _bob, 1_000m);

        Assert.Equal(9_000m, _alice.Balance);
        Assert.Equal(1_000m, _bob.Balance);
    }

    [Fact]
    public void A_transfer_above_the_limit_is_refused()
    {
        Assert.Throws<BusinessRuleException>(() => _service.Transfer(_alice, _bob, 5_000.01m));
        Assert.Equal(10_000m, _alice.Balance);
    }

    [Fact]
    public void A_transfer_to_oneself_is_refused()
    {
        Assert.Throws<BusinessRuleException>(() => _service.Transfer(_alice, _alice, 10m));
    }
}

using Onion.Domain.Model;
using Onion.Domain.Services;

namespace Onion.Tests.Domain;

// Les couches centrales se testent sans aucun double : ce sont des objets purs.
public class ServiceVirementTests
{
    private readonly ServiceVirement _service = new();
    private readonly Compte _alice = new(Guid.NewGuid(), "Alice", 10_000m);
    private readonly Compte _bob = new(Guid.NewGuid(), "Bob", 0m);

    [Fact]
    public void Un_virement_deplace_l_argent()
    {
        _service.Virer(_alice, _bob, 1_000m);

        Assert.Equal(9_000m, _alice.Solde);
        Assert.Equal(1_000m, _bob.Solde);
    }

    [Fact]
    public void Un_virement_au_dela_du_plafond_est_refuse()
    {
        Assert.Throws<RegleMetierException>(() => _service.Virer(_alice, _bob, 5_000.01m));
        Assert.Equal(10_000m, _alice.Solde);
    }

    [Fact]
    public void Un_virement_vers_soi_meme_est_refuse()
    {
        Assert.Throws<RegleMetierException>(() => _service.Virer(_alice, _alice, 10m));
    }
}

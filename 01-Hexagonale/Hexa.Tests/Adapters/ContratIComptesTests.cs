using Hexa.Adapters.Persistance;
using Hexa.Hexagone.Domaine;
using Hexa.Hexagone.Ports.Sortants;

namespace Hexa.Tests.Adapters;

/// <summary>
/// Tests de contrat : les mêmes tests sont exécutés sur chaque adapter du port IComptes.
/// C'est ce qui garantit qu'on peut remplacer un adapter par un autre sans surprise.
/// </summary>
public abstract class ContratIComptes
{
    protected abstract IComptes Creer(params Compte[] comptesInitiaux);

    [Fact]
    public async Task Charger_un_compte_inconnu_renvoie_null()
    {
        Assert.Null(await Creer().Charger(Guid.NewGuid()));
    }

    [Fact]
    public async Task Un_compte_sauvegarde_est_relu_a_l_identique()
    {
        var id = Guid.NewGuid();
        var comptes = Creer(new Compte(id, "Alice", 100m));

        var compte = (await comptes.Charger(id))!;
        compte.Retirer(40m);
        await comptes.Sauvegarder(compte);

        var relu = (await comptes.Charger(id))!;
        Assert.Equal("Alice", relu.Titulaire);
        Assert.Equal(60m, relu.Solde);
    }

    [Fact]
    public async Task Une_modification_non_sauvegardee_n_est_pas_persistee()
    {
        var id = Guid.NewGuid();
        var comptes = Creer(new Compte(id, "Alice", 100m));

        (await comptes.Charger(id))!.Retirer(40m);

        Assert.Equal(100m, (await comptes.Charger(id))!.Solde);
    }
}

public class ComptesEnMemoireTests : ContratIComptes
{
    protected override IComptes Creer(params Compte[] comptesInitiaux) => new ComptesEnMemoire(comptesInitiaux);
}

public class ComptesFichierJsonTests : ContratIComptes, IDisposable
{
    private readonly string _fichier = Path.Combine(Path.GetTempPath(), $"comptes-{Guid.NewGuid()}.json");

    protected override IComptes Creer(params Compte[] comptesInitiaux) => new ComptesFichierJson(_fichier, comptesInitiaux);

    public void Dispose() => File.Delete(_fichier);
}

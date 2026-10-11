using Ihm.Banque;

namespace Ihm.Tests;

/// <summary>Le modèle seul : les règles que les deux interfaces affichent, mais que seule la banque fait respecter.</summary>
public class BanqueTests
{
    private readonly BanqueEnMemoire _banque = new(new HorlogeFixe());

    [Fact]
    public async Task Un_virement_debite_la_source_et_credite_la_cible()
    {
        var virement = await _banque.VirerAsync(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, 250.50m);

        Assert.Equal((749.50m, 750.50m), (virement.SoldeSource, virement.SoldeCible));
        var operations = await _banque.OperationsAsync(BanqueEnMemoire.Bob);
        Assert.Equal("Virement de Alice (Compte courant)", operations[0].Libelle);   // la plus récente d'abord
        Assert.Equal("11/10/2026 14:30", Format.Date(operations[0].Date));         // à l'heure de Paris
    }

    [Theory]
    [InlineData("0", "Le montant doit être positif.")]
    [InlineData("-5", "Le montant doit être positif.")]
    [InlineData("12.345", "Le montant se donne au centime près.")]
    [InlineData("1000.01", "Solde insuffisant : 1 000,00 € disponibles.")]
    public async Task La_banque_refuse_ce_que_ses_regles_interdisent(string texte, string message)
    {
        var montant = decimal.Parse(texte, System.Globalization.CultureInfo.InvariantCulture);
        var refus = await Assert.ThrowsAsync<ErreurMetier>(() => _banque.VirerAsync(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, montant));

        Assert.Equal(message, refus.Message);
        Assert.Equal(1_000m, (await _banque.CompteAsync(BanqueEnMemoire.Alice)).Solde);
    }

    [Fact]
    public async Task Un_compte_ne_se_vire_rien_a_lui_meme_et_un_compte_inconnu_est_refuse()
    {
        var memeCompte = await Assert.ThrowsAsync<ErreurMetier>(() => _banque.VirerAsync(BanqueEnMemoire.Alice, BanqueEnMemoire.Alice, 10m));
        await Assert.ThrowsAsync<ErreurMetier>(() => _banque.VirerAsync(BanqueEnMemoire.Alice, Guid.NewGuid(), 10m));

        Assert.Equal("Choisissez deux comptes différents.", memeCompte.Message);
        Assert.Equal(1_000m, (await _banque.CompteAsync(BanqueEnMemoire.Alice)).Solde);
    }
}

using Ddd.TenueDeCompte.Domaine;
using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.Tests;

public class ObjetsValeursTests
{
    private static Argent Euros(decimal montant) => new(montant, Devise.Euro);

    [Fact]
    public void Deux_objets_valeurs_identiques_sont_egaux()
    {
        Assert.Equal(Euros(10m), Euros(10.00m));
        Assert.NotEqual(Euros(10m), new Argent(10m, Devise.DollarUs));
    }

    [Fact]
    public void On_n_additionne_pas_des_euros_et_des_dollars()
    {
        Assert.Throws<RegleMetierException>(() => Euros(10m) + new Argent(10m, Devise.DollarUs));
        Assert.Throws<RegleMetierException>(() => Euros(10m) > new Argent(1m, Devise.DollarUs));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.234")]
    public void Un_montant_invalide_ne_peut_pas_exister(string montant)
    {
        Assert.Throws<RegleMetierException>(() => Euros(decimal.Parse(montant, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Un_iban_valide_est_normalise_et_affiche_par_blocs()
    {
        var iban = Iban.Depuis("fr76 3000 6000 0112 3456 7890 189");

        Assert.Equal("FR7630006000011234567890189", iban.Valeur);
        Assert.Equal("FR76 3000 6000 0112 3456 7890 189", iban.ToString());
    }

    [Theory]
    [InlineData("FR77 3000 6000 0112 3456 7890 189")]  // clé de contrôle fausse
    [InlineData("pas un iban")]
    public void Un_iban_invalide_ne_peut_pas_exister(string saisie)
    {
        Assert.Throws<RegleMetierException>(() => Iban.Depuis(saisie));
    }

    [Fact]
    public void Un_iban_genere_a_une_cle_de_controle_valide()
    {
        var iban = Iban.Generer("FR", "3000100001000000000420" + "0");

        Assert.Equal(iban, Iban.Depuis(iban.Valeur));
    }

    [Fact]
    public void Le_nom_du_titulaire_est_nettoye()
    {
        Assert.Equal("Alice Martin", NomTitulaire.Depuis("  Alice   Martin ").Valeur);
    }
}

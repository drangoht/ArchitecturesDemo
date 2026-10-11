using Fig.Nouveau;

namespace Fig.Tests;

/// <summary>La traduction entre les deux modèles, sans aucun serveur.</summary>
public class CoucheAntiCorruptionTests
{
    private static LigneJournalAncien Ligne(string type = "OPERATION", string date = "12/10/2026 10:00:00", long centimes = -12_345,
        string origine = "ANCIEN") =>
        new(7, type, "10001", null, "A-000007", date, "FRAIS DE TENUE DE COMPTE", centimes, origine);

    [Fact]
    public void Une_ligne_du_journal_devient_une_operation_en_euros_avec_son_fuseau()
    {
        var fait = Assert.IsType<OperationEnregistree>(TraducteurAncien.Traduire(Ligne()));

        Assert.Equal("10001", fait.Numero);
        Assert.Equal(-123.45m, fait.Operation.Montant);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), fait.Operation.Date);   // 10 h à Paris = 8 h UTC
        Assert.Equal(TimeSpan.FromHours(2), fait.Operation.Date.Offset);
        Assert.Equal(Origine.Ancien, fait.Operation.Origine);
    }

    [Fact]
    public void L_heure_de_Paris_n_a_pas_le_meme_decalage_en_hiver()
    {
        var fait = Assert.IsType<OperationEnregistree>(TraducteurAncien.Traduire(Ligne(date: "15/01/2026 10:00:00")));

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), fait.Operation.Date);
    }

    [Fact]
    public void Une_ouverture_de_compte_devient_un_compte()
    {
        var ouverture = new LigneJournalAncien(1, "OUVERTURE", "10001", "ALICE MARTIN", null, null, null, 0, "ANCIEN");

        Assert.Equal(new CompteOuvert("10001", "ALICE MARTIN"), TraducteurAncien.Traduire(ouverture));
    }

    [Fact]
    public void Un_type_inconnu_arrete_la_traduction_au_lieu_d_etre_ignore()
    {
        Assert.Throws<FormatException>(() => TraducteurAncien.Traduire(Ligne(type: "AGIOS")));
    }

    [Fact]
    public void Une_operation_du_nouveau_fait_l_aller_retour_sans_perte()
    {
        var operation = new Operation("N-1", new DateTimeOffset(2026, 10, 12, 8, 30, 15, TimeSpan.Zero), "VIR VERS 10002", -50.5m, Origine.Nouveau);

        var versAncien = TraducteurAncien.VersAncien(new OperationAReporter("10001", operation));
        Assert.Equal("12/10/2026 10:30:15", versAncien.Date);
        Assert.Equal(-5_050, versAncien.MontantCentimes);

        var retour = new LigneJournalAncien(9, "OPERATION", versAncien.Numero, null, versAncien.IdOperation, versAncien.Date,
            versAncien.Libelle, versAncien.MontantCentimes, "NOUVEAU");
        var fait = Assert.IsType<OperationEnregistree>(TraducteurAncien.Traduire(retour));
        Assert.Equal(operation, fait.Operation);
    }

    [Fact]
    public void Un_montant_au_dela_du_centime_ne_peut_pas_partir_vers_l_ancien()
    {
        Assert.Throws<ArgumentException>(() => TraducteurAncien.Centimes(1.005m));
    }
}

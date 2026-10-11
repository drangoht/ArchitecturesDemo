using System.ComponentModel;
using Ihm.Banque;
using Ihm.Presentation;

namespace Ihm.Tests;

/// <summary>
/// Le comportement complet de l'écran, sans écran : ce que verrait l'utilisateur se lit dans les propriétés du
/// ViewModel, et ce qu'il ferait s'écrit dedans. C'est la promesse de MVVM.
/// </summary>
public class ViewModelTests
{
    private readonly BanqueTelecommandee _banque = new();
    private readonly ConfirmationProgrammee _confirmation = new(reponse: true);
    private readonly EcranPrincipalViewModel _ecran;

    public ViewModelTests() => _ecran = new EcranPrincipalViewModel(_banque, _confirmation);

    private VirementViewModel Virement => _ecran.Virement;

    private CompteViewModel Compte(Guid id) => _ecran.Comptes.Comptes.Single(c => c.Id == id);

    private async Task Preparer(Guid source, Guid cible, string montant)
    {
        await _ecran.InitialiserAsync();
        Virement.Source = Compte(source);
        Virement.Cible = Compte(cible);
        Virement.MontantSaisi = montant;
    }

    [Fact]
    public async Task Au_demarrage_les_comptes_sont_charges_et_le_premier_selectionne()
    {
        await _ecran.InitialiserAsync();

        Assert.Equal(["Alice — Compte courant", "Alice — Livret A", "Bob — Compte courant"], _ecran.Comptes.Comptes.Select(c => c.Affichage));
        Assert.Equal("1 000,00 €", _ecran.Comptes.Comptes[0].SoldeAffiche);
        Assert.Same(_ecran.Comptes.Comptes[0], _ecran.Comptes.CompteSelectionne);
        Assert.Equal("Opérations — Alice — Compte courant", _ecran.Comptes.TitreOperations);
        Assert.Equal("+1 000,00 €", Assert.Single(_ecran.Comptes.Operations).Montant);
        Assert.False(Virement.VirerCommande.CanExecute(null));   // formulaire vide : bouton grisé
    }

    [Fact]
    public async Task Chaque_frappe_est_verifiee_et_la_vue_en_est_prevenue()
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "");
        var proprietes = new List<string?>();
        var erreurs = new List<string?>();
        Virement.PropertyChanged += (_, e) => proprietes.Add(e.PropertyName);
        Virement.ErrorsChanged += (_, e) => erreurs.Add(e.PropertyName);

        Virement.MontantSaisi = "abc";

        // Ce qu'une liaison WPF écoute : la saisie, le texte de l'erreur, l'état d'erreur ; et la bordure rouge (ErrorsChanged).
        Assert.Equal(["MontantSaisi", "ErreurMontant", "HasErrors"], proprietes);
        Assert.Equal(["MontantSaisi"], erreurs);
        Assert.Equal("Saisissez un montant, par exemple 250,50.", Virement.ErreurMontant);
        Assert.Equal(["Saisissez un montant, par exemple 250,50."], Virement.GetErrors("MontantSaisi").Cast<string>());
        Assert.Equal("abc", Virement.MontantSaisi);   // ce qui a été tapé reste affiché

        proprietes.Clear();
        Virement.MontantSaisi = "abcd";               // même erreur : rien à redessiner
        Assert.Equal(["MontantSaisi"], proprietes);

        proprietes.Clear();
        Virement.MontantSaisi = "abcd";               // même valeur (WPF réécrit souvent la valeur affichée) : aucun signal
        Assert.Empty(proprietes);
    }

    [Fact]
    public async Task Le_bouton_Virer_suit_la_saisie()
    {
        await _ecran.InitialiserAsync();
        var etats = new List<bool>();
        Virement.VirerCommande.CanExecuteChanged += (_, _) => etats.Add(Virement.VirerCommande.CanExecute(null));

        Virement.Source = Compte(BanqueEnMemoire.Alice);
        Virement.Cible = Compte(BanqueEnMemoire.Bob);
        Virement.MontantSaisi = "250";
        Virement.MontantSaisi = "2500";
        Virement.MontantSaisi = "25";

        // Prévenu à chaque changement ; actif seulement quand source, cible et montant sont là et justes.
        Assert.Equal([false, false, true, false, true], etats);
    }

    [Theory]
    [InlineData("250", "250")]
    [InlineData("250,5", "250.5")]
    [InlineData("250.50", "250.50")]
    [InlineData("1 000", "1000")]
    [InlineData(" 1 000,00 € ", "1000.00")]
    public async Task Les_montants_se_tapent_a_la_francaise(string saisie, string attendu)
    {
        await Preparer(BanqueEnMemoire.LivretAlice, BanqueEnMemoire.Bob, saisie);

        Assert.Null(Virement.ErreurMontant);
        Assert.True(Virement.VirerCommande.CanExecute(null));
        await Virement.VirerCommande.ExecuterAsync();
        Assert.Equal(8_000m - decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture), Compte(BanqueEnMemoire.LivretAlice).Solde);
    }

    [Theory]
    [InlineData("-5", "Le montant doit être positif.")]
    [InlineData("0", "Le montant doit être positif.")]
    [InlineData("12,345", "Le montant se donne au centime près.")]
    [InlineData("1 000,01", "Solde insuffisant : 1 000,00 € disponibles.")]
    [InlineData("12 abc", "Saisissez un montant, par exemple 250,50.")]
    public async Task Les_erreurs_s_affichent_avant_meme_d_envoyer(string saisie, string erreur)
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, saisie);

        Assert.Equal(erreur, Virement.ErreurMontant);
        Assert.False(Virement.VirerCommande.CanExecute(null));
        Assert.Equal(0, _banque.VirementsDemandes);
    }

    [Fact]
    public async Task Deux_fois_le_meme_compte_est_signale_et_Echanger_inverse_les_comptes()
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Alice, "10");
        Assert.Equal("Choisissez deux comptes différents.", Virement.ErreurCible);

        Virement.Cible = Compte(BanqueEnMemoire.Bob);
        Assert.Null(Virement.ErreurCible);
        Virement.EchangerCommande.Execute(null);

        Assert.Equal((BanqueEnMemoire.Bob, BanqueEnMemoire.Alice), (Virement.Source!.Id, Virement.Cible!.Id));
    }

    [Fact]
    public async Task Le_solde_de_la_source_est_suivi_meme_quand_il_change_ailleurs()
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "900");
        Assert.Null(Virement.ErreurMontant);

        // Un virement fait ailleurs (une autre fenêtre, un autre appareil), puis une actualisation de la liste.
        await _banque.Reelle.VirerAsync(BanqueEnMemoire.Alice, BanqueEnMemoire.LivretAlice, 200m);
        await _ecran.Comptes.ActualiserAsync();

        Assert.Equal("Solde insuffisant : 800,00 € disponibles.", Virement.ErreurMontant);

        // Une fois la source changée, l'ancienne n'est plus suivie (sinon : fuite et erreurs fantômes).
        Virement.Source = Compte(BanqueEnMemoire.LivretAlice);
        var changements = 0;
        Virement.PropertyChanged += (_, _) => changements++;
        Virement.VirerCommande.CanExecuteChanged += (_, _) => changements++;
        await _banque.Reelle.VirerAsync(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, 100m);
        await _ecran.Comptes.ActualiserAsync();
        Assert.Equal(0, changements);
    }

    [Fact]
    public async Task Pendant_le_virement_l_ecran_attend_et_un_double_clic_ne_vire_pas_deux_fois()
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "250,50");
        var aliceAffichee = Compte(BanqueEnMemoire.Alice);
        _banque.Retenir("Virer");

        var clic = Virement.VirerCommande.ExecuterAsync();
        Assert.True(Virement.EstOccupe);
        Assert.Equal("Virement en cours…", Virement.Message);
        Assert.False(Virement.VirerCommande.CanExecute(null));
        Virement.VirerCommande.Execute(null);                 // deuxième clic, bouton grisé : ignoré

        _banque.Relacher("Virer");
        await clic;

        Assert.Equal(1, _banque.VirementsDemandes);
        Assert.False(Virement.EstOccupe);
        Assert.Equal("Virement de 250,50 € effectué. Nouveau solde : 749,50 €.", Virement.Message);
        Assert.False(Virement.MessageEstErreur);
        Assert.Equal("", Virement.MontantSaisi);
        // Soldes actualisés SUR PLACE : le même objet, donc la sélection et les listes ne bougent pas.
        Assert.Same(aliceAffichee, Compte(BanqueEnMemoire.Alice));
        Assert.Equal("749,50 €", aliceAffichee.SoldeAffiche);
        Assert.Equal("-250,50 €", _ecran.Comptes.Operations[0].Montant);   // l'historique affiché aussi
    }

    [Fact]
    public async Task Un_refus_de_la_banque_s_affiche_comme_une_erreur()
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "900");
        // Entre la saisie et le clic, le compte a été vidé ailleurs, et l'écran ne le sait pas encore.
        await _banque.Reelle.VirerAsync(BanqueEnMemoire.Alice, BanqueEnMemoire.LivretAlice, 500m);

        await Virement.VirerCommande.ExecuterAsync();

        Assert.Equal("Solde insuffisant : 500,00 € disponibles.", Virement.Message);
        Assert.True(Virement.MessageEstErreur);
        Assert.False(Virement.EstOccupe);
        Assert.Equal("900", Virement.MontantSaisi);              // la saisie est gardée
        Assert.True(Virement.VirerCommande.CanExecute(null));    // et le bouton réactivé
    }

    [Theory]
    [InlineData(false, "Virement annulé.", 8_000)]
    [InlineData(true, "Virement de 1 500,00 € effectué. Nouveau solde : 6 500,00 €.", 6_500)]
    public async Task Un_gros_montant_demande_confirmation(bool reponse, string message, int soldeLivret)
    {
        var confirmation = new ConfirmationProgrammee(reponse);
        var ecran = new EcranPrincipalViewModel(_banque, confirmation);
        await ecran.InitialiserAsync();
        ecran.Virement.Source = ecran.Comptes.Comptes[1];   // Livret A
        ecran.Virement.Cible = ecran.Comptes.Comptes[2];    // Bob
        ecran.Virement.MontantSaisi = "1500";

        await ecran.Virement.VirerCommande.ExecuterAsync();

        Assert.Equal("Virer 1 500,00 € de Alice — Livret A vers Bob — Compte courant ?", Assert.Single(confirmation.Questions));
        Assert.Equal(message, ecran.Virement.Message);
        Assert.Equal(soldeLivret, (await _banque.Reelle.CompteAsync(BanqueEnMemoire.LivretAlice)).Solde);
    }

    [Fact]
    public async Task Un_petit_montant_ne_demande_rien()
    {
        await Preparer(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "999,99");

        await Virement.VirerCommande.ExecuterAsync();

        Assert.Empty(_confirmation.Questions);
        Assert.Equal(1, _banque.VirementsDemandes);
    }

    [Fact]
    public async Task Une_reponse_qui_arrive_trop_tard_n_ecrase_pas_la_selection_suivante()
    {
        await _ecran.InitialiserAsync();
        var comptes = _ecran.Comptes;

        // Clic sur Bob, puis aussitôt sur le Livret ; les deux réponses tardent.
        _banque.Retenir("Operations", BanqueEnMemoire.Bob);
        _banque.Retenir("Operations", BanqueEnMemoire.LivretAlice);
        comptes.CompteSelectionne = Compte(BanqueEnMemoire.Bob);
        var chargementBob = comptes.ChargementOperations;
        comptes.CompteSelectionne = Compte(BanqueEnMemoire.LivretAlice);
        var chargementLivret = comptes.ChargementOperations;

        // La réponse pour Bob arrive la première, mais elle est périmée : ignorée, et le Livret est toujours attendu.
        _banque.Relacher("Operations", BanqueEnMemoire.Bob);
        await chargementBob;
        Assert.Empty(comptes.Operations);
        Assert.True(comptes.ChargementEnCours);

        _banque.Relacher("Operations", BanqueEnMemoire.LivretAlice);
        await chargementLivret;

        Assert.Equal("Opérations — Alice — Livret A", comptes.TitreOperations);
        Assert.Equal("+8 000,00 €", Assert.Single(comptes.Operations).Montant);
        Assert.False(comptes.ChargementEnCours);
    }
}

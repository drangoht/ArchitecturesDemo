using Ihm.Banque;
using Ihm.Console;
using Ihm.Presentation;

namespace Ihm.Tests;

/// <summary>La deuxième vue : la même logique, branchée autrement. Et ce qu'est une liaison, démontée.</summary>
public class VueConsoleTests
{
    [Fact]
    public void Une_liaison_recopie_la_propriete_a_chaque_changement_et_plus_apres_Dispose()
    {
        var virement = new EcranPrincipalViewModel(new BanqueEnMemoire(new HorlogeFixe()), new ConfirmationProgrammee(true)).Virement;
        var affiche = new List<object?>();

        var liaison = Liaison.Lier(virement, nameof(VirementViewModel.MontantSaisi), affiche.Add);
        virement.MontantSaisi = "12";
        virement.Cible = null;                  // autre propriété : ignorée
        virement.MontantSaisi = "13";
        liaison.Dispose();
        virement.MontantSaisi = "14";

        Assert.Equal(["", "12", "13"], affiche);   // la valeur initiale, puis chaque changement
    }

    [Fact]
    public void Une_liaison_vers_une_propriete_qui_n_existe_pas_echoue_franchement()
    {
        var virement = new EcranPrincipalViewModel(new BanqueEnMemoire(new HorlogeFixe()), new ConfirmationProgrammee(true)).Virement;

        var erreur = Assert.Throws<ArgumentException>(() => Liaison.Lier(virement, "Montant", _ => { }));
        Assert.StartsWith("VirementViewModel n'a pas de propriété « Montant ».", erreur.Message);
    }

    [Fact]
    public async Task La_console_pilote_les_memes_ViewModels_que_la_fenetre_WPF()
    {
        var entree = new StringReader(string.Join('\n', "de 2", "vers 3", "montant 1500", "virer", "n", "montant 25", "virer", "voir 3", "quitter"));
        var sortie = new StringWriter();
        var banque = new BanqueEnMemoire(new HorlogeFixe());
        var ecran = new EcranPrincipalViewModel(banque, new ConfirmationConsole(entree, sortie));
        using var vue = new VueConsole(ecran, entree, sortie);

        await vue.ExecuterAsync();

        var lignes = sortie.ToString().Split(Environment.NewLine).Select(l => l.TrimEnd()).ToArray();
        string[] attendu =
        [
            "> montant 1500",
            "  [Virer] actif",                                       // poussé par CanExecuteChanged, sans être demandé
            "> virer",
            "  [Virer] grisé",                                       // pendant l'exécution
            "  ? Virer 1 500,00 € de Alice — Livret A vers Bob — Compte courant ? (o/n)",
            "> n",
            "  » Virement annulé.",                                  // poussé par la liaison sur Message
            "  [Virer] actif",                                       // la commande est finie
            "> montant 25",
            "> virer",
            "  [Virer] grisé",
            "  » Virement en cours…",
            "  » Virement de 25,00 € effectué. Nouveau solde : 7 975,00 €.",
            "> voir 3",
            "  Opérations — Bob — Compte courant",
        ];
        Assert.Equal(attendu, lignes.SkipWhile(l => l != "> montant 1500").Take(attendu.Length));
        Assert.Contains(lignes, l => l.StartsWith("    11/10/2026 14:30  Virement de Alice (Livret A)") && l.EndsWith("+25,00 €"));
        Assert.Equal(525m, (await banque.CompteAsync(BanqueEnMemoire.Bob)).Solde);
    }
}

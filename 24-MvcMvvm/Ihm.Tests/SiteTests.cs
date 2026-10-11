using System.Net;
using System.Text.RegularExpressions;
using Ihm.Banque;
using Ihm.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ihm.Tests;

/// <summary>
/// Le site MVC complet, en mémoire : routage, liaison de modèle, validation, jeton anti-falsification, vues Razor,
/// redirection et TempData. On joue le rôle du navigateur : on lit du HTML et on renvoie des champs de formulaire.
/// </summary>
public class SiteTests : IDisposable
{
    private readonly WebApplicationFactory<Marqueur> _site = new();
    private readonly HttpClient _navigateur;

    public SiteTests() =>
        // Les cookies sont gardés (jeton anti-falsification, TempData) ; les redirections, on les suit à la main.
        _navigateur = _site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void Dispose() => _site.Dispose();

    private IBanque Banque => _site.Services.GetRequiredService<IBanque>();

    private async Task<string> Page(string url)
    {
        var reponse = await _navigateur.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        return await reponse.Content.ReadAsStringAsync();
    }

    /// <summary>Ce que fait un navigateur : lire le formulaire (et son jeton caché), puis l'envoyer.</summary>
    private async Task<HttpResponseMessage> Envoyer(Dictionary<string, string> champs, bool avecJeton = true)
    {
        var formulaire = await Page("/Virements/Nouveau");
        if (avecJeton)
            champs["__RequestVerificationToken"] = Regex.Match(formulaire, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        return await _navigateur.PostAsync("/Virements/Nouveau", new FormUrlEncodedContent(champs));
    }

    private static Dictionary<string, string> Champs(Guid source, Guid cible, string montant) => new()
    {
        ["Source"] = source.ToString(), ["Cible"] = cible.ToString(), ["Montant"] = montant,
    };

    [Fact]
    public async Task La_page_d_accueil_liste_les_comptes()
    {
        var html = await Page("/");

        Assert.Contains("<td class=\"montant\">8 000,00 €</td>", html);
        Assert.Contains($"href=\"/Comptes/Detail/{BanqueEnMemoire.Bob}\"", html);   // lien produit par le tag helper asp-action
    }

    [Fact]
    public async Task Un_compte_inconnu_donne_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _navigateur.GetAsync($"/Comptes/Detail/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Le_formulaire_preselectionne_le_compte_d_origine()
    {
        var html = await Page($"/Virements/Nouveau?source={BanqueEnMemoire.Bob}");

        Assert.Contains($"<option selected=\"selected\" value=\"{BanqueEnMemoire.Bob}\">Bob — Compte courant (500,00 €)</option>", html);
    }

    [Fact]
    public async Task Sans_jeton_anti_falsification_le_formulaire_est_refuse()
    {
        var reponse = await Envoyer(Champs(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "10"), avecJeton: false);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(1_000m, (await Banque.CompteAsync(BanqueEnMemoire.Alice)).Solde);
    }

    [Fact]
    public async Task Post_Redirect_Get_le_message_s_affiche_une_seule_fois()
    {
        var reponse = await Envoyer(Champs(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "250,50"));

        Assert.Equal(HttpStatusCode.Redirect, reponse.StatusCode);
        var arrivee = reponse.Headers.Location!.ToString();
        Assert.Equal($"/Comptes/Detail/{BanqueEnMemoire.Alice}", arrivee);

        var html = await Page(arrivee);
        Assert.Contains("Virement de 250,50 € effectué. Nouveau solde : 749,50 €.", html);
        Assert.Contains("<strong id=\"solde\">749,50 €</strong>", html);

        // Actualiser la page (F5) relit la page d'arrivée : pas de second virement, et le message a été consommé.
        var html2 = await Page(arrivee);
        Assert.DoesNotContain("effectué", html2);
        Assert.Equal(749.50m, (await Banque.CompteAsync(BanqueEnMemoire.Alice)).Solde);
    }

    [Fact]
    public async Task Une_saisie_invalide_reaffiche_le_formulaire_avec_ce_qui_a_ete_tape()
    {
        var reponse = await Envoyer(Champs(BanqueEnMemoire.Alice, BanqueEnMemoire.Bob, "abc"));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);   // pas de redirection : la même vue, avec les erreurs
        var html = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("« abc » n'est pas un montant valide.", WebUtility.HtmlDecode(html));   // l'apostrophe est encodée
        Assert.Matches("<input[^>]*class=\"input-validation-error\"[^>]*value=\"abc\"", html);
        Assert.Contains($"<option selected=\"selected\" value=\"{BanqueEnMemoire.Bob}\">", html);   // les choix sont gardés
    }

    [Fact]
    public async Task Les_champs_manquants_et_les_regles_entre_champs_sont_verifies()
    {
        var vide = await (await Envoyer([])).Content.ReadAsStringAsync();
        Assert.Contains("Choisissez le compte à débiter.", vide);
        Assert.Contains("Saisissez un montant.", vide);

        var memeCompte = await (await Envoyer(Champs(BanqueEnMemoire.Alice, BanqueEnMemoire.Alice, "10"))).Content.ReadAsStringAsync();
        // Signalé sur le champ Cible par IValidatableObject, avant d'appeler la banque (qui refuserait aussi, mais en tête de page).
        Assert.Matches("data-valmsg-for=\"Cible\"[^>]*>Choisissez deux comptes différents.</span>", memeCompte);
    }

    [Fact]
    public async Task Un_refus_de_la_banque_s_affiche_en_tete_du_formulaire()
    {
        var reponse = await Envoyer(Champs(BanqueEnMemoire.Bob, BanqueEnMemoire.Alice, "600"));

        var html = await reponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Matches("validation-summary-errors[\\s\\S]*Solde insuffisant : 500,00 € disponibles.", html);
    }

    [Fact]
    public async Task Un_gros_montant_demande_une_page_de_confirmation_qui_renvoie_tout()
    {
        var etape1 = await Envoyer(Champs(BanqueEnMemoire.LivretAlice, BanqueEnMemoire.Bob, "1500"));
        var confirmation = await etape1.Content.ReadAsStringAsync();
        Assert.Contains("<h2>Confirmer le virement</h2>", confirmation);
        Assert.Equal(8_000m, (await Banque.CompteAsync(BanqueEnMemoire.LivretAlice)).Solde);

        // Le serveur n'a rien gardé : la page de confirmation renvoie les champs cachés, plus Confirme=true.
        var champs = Regex.Matches(confirmation, "<input type=\"hidden\"[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"|<input[^>]*name=\"([^\"]+)\" type=\"hidden\" value=\"([^\"]*)\"")
            .ToDictionary(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value,
                          m => m.Groups[1].Success ? m.Groups[2].Value : m.Groups[4].Value);
        Assert.Equal(["Source", "Cible", "Montant", "Confirme", "__RequestVerificationToken"], champs.Keys);
        var etape2 = await _navigateur.PostAsync("/Virements/Nouveau", new FormUrlEncodedContent(champs));

        Assert.Equal(HttpStatusCode.Redirect, etape2.StatusCode);
        Assert.Equal(6_500m, (await Banque.CompteAsync(BanqueEnMemoire.LivretAlice)).Solde);
    }
}

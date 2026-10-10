using Mk.Contrats;
using Mk.Noyau;

namespace Mk.Tests;

public class HorlogeReglable(DateTime heureLocale) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => new(heureLocale, TimeSpan.Zero);
}

/// <summary>Le noyau se teste seul, avec des plugins « en mémoire » écrits dans le test : aucune DLL nécessaire.</summary>
public class NoyauTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid();
    private readonly RegistrePlugins _registre = new();
    private readonly NoyauBancaire _noyau;

    public NoyauTests()
    {
        _noyau = new NoyauBancaire(_registre, new HorlogeReglable(new DateTime(2026, 10, 12, 14, 0, 0)), _ => { });
        _noyau.Ouvrir(Alice, "Alice", 100m);
        _noyau.Ouvrir(Bob, "Bob", 0m);
    }

    [Fact]
    public void Sans_aucun_plugin_le_noyau_sait_deja_faire_un_virement()
    {
        var resultat = _noyau.Virer(Alice, Bob, 30m);

        Assert.True(resultat.Succes);
        Assert.Equal((0m, 70m), (resultat.Frais, resultat.Solde));
    }

    [Fact]
    public void Une_regle_de_frais_s_ajoute_au_montant_et_apparait_au_releve()
    {
        _registre.Enregistrer(new FraisFixes(1m));

        var resultat = _noyau.Virer(Alice, Bob, 30m);

        Assert.Equal((1m, 69m), (resultat.Frais, resultat.Solde));
        Assert.Contains(_noyau.Consulter(Alice)!.Lignes, l => l.Libelle == "Frais fixes" && l.Montant == -1m);
    }

    [Fact]
    public void Un_plugin_qui_plante_est_desactive_et_n_emporte_pas_le_noyau()
    {
        var defectueux = _registre.Enregistrer(new ControleDefectueux());

        var premier = _noyau.Retirer(Alice, 10m);
        var second = _noyau.Retirer(Alice, 10m);

        Assert.False(premier.Succes);  // refus par sécurité
        Assert.Contains("en erreur", premier.Erreur);
        Assert.Equal(EtatPlugin.EnErreur, defectueux.Etat);
        Assert.True(second.Succes);  // le plugin est écarté, le noyau continue
    }

    [Fact]
    public void Un_plugin_qui_exige_un_noyau_plus_recent_est_refuse()
    {
        var futur = _registre.Enregistrer(new PluginDuFutur());

        Assert.Equal(EtatPlugin.Incompatible, futur.Etat);
        Assert.True(_noyau.Retirer(Alice, 10m).Succes);  // il n'est jamais appelé
    }

    [Fact]
    public void Sans_plugin_de_format_le_noyau_ne_sait_pas_exporter()
    {
        Assert.Null(_noyau.Exporter(Alice, "csv", out var formats));
        Assert.Empty(formats);
    }

    private sealed class FraisFixes(decimal montant) : IRegleFrais
    {
        public string Nom => "FraisFixes";
        public string Description => "test";
        public string Libelle => "Frais fixes";
        public decimal CalculerFrais(OperationProposee operation) => montant;
    }

    private sealed class ControleDefectueux : IControleOperation
    {
        public string Nom => "Defectueux";
        public string Description => "test";
        public string? Verifier(OperationProposee operation) => throw new InvalidOperationException("Bug dans le plugin");
    }

    private sealed class PluginDuFutur : IControleOperation
    {
        public string Nom => "DuFutur";
        public string Description => "test";
        public Version VersionNoyauMinimale => new(2, 0);
        public string? Verifier(OperationProposee operation) => "ne doit jamais être appelé";
    }
}

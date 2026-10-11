using Acteurs.Noyau;

namespace Acteurs.Banque;

/// <summary>
/// UN acteur par virement, identifié par l'identifiant du virement. Il n'y a pas de transaction entre deux comptes
/// (deux acteurs, deux états séparés) : le virement converse avec eux, étape par étape, et note où il en est.
/// <list type="bullet">
/// <item>Son identité fait l'idempotence : rejouer « virement/v-42 » réveille le MÊME acteur, qui connaît déjà la réponse.</item>
/// <item>Il n'y a pas d'interblocage possible : les comptes ne demandent jamais rien à personne, seul le virement attend
/// leurs réponses. Avec des verrous, A→B et B→A simultanés exigeraient de verrouiller dans un ordre fixe.</item>
/// </list>
/// </summary>
public sealed class VirementActeur(MagasinEtats magasin, Pieges pieges) : Acteur
{
    public const string Type = "virement";

    public sealed class Etat
    {
        public Guid Source { get; set; }
        public Guid Cible { get; set; }
        public decimal Montant { get; set; }
        public StatutVirement Statut { get; set; }
        public string? Motif { get; set; }
    }

    private Etat? _etat;

    public override async Task Demarrer() => _etat = await magasin.Lire<Etat>(Contexte.Chemin);

    public override async Task<object?> Recevoir(object message) => message switch
    {
        ExecuterVirement m => await Executer(m),
        ConsulterVirement => _etat is null ? new Refus($"Virement {Contexte.Cle} introuvable.", Introuvable: true) : Vue(),
        _ => throw new InvalidOperationException($"Message inattendu : {message.GetType().Name}.")
    };

    private async Task<object> Executer(ExecuterVirement m)
    {
        if (_etat is not null && (_etat.Source, _etat.Cible, _etat.Montant) != (m.Source, m.Cible, m.Montant))
            return new Refus($"L'identifiant {Contexte.Cle} désigne déjà un autre virement.");
        if (_etat is { Statut: StatutVirement.Termine or StatutVirement.Refuse or StatutVirement.Annule })
            return Vue();  // déjà fini : même réponse, aucun effet
        if (m.Source == m.Cible)
            return new Refus("Les comptes source et cible doivent être différents.");
        if (m.Montant <= 0)
            return new Refus("Le montant doit être positif.");

        if (_etat is null)
        {
            _etat = new Etat { Source = m.Source, Cible = m.Cible, Montant = m.Montant, Statut = StatutVirement.EnCours };
            await Sauver();
        }

        var source = Contexte.Acteur(CompteActeur.Type, m.Source.ToString());
        var cible = Contexte.Acteur(CompteActeur.Type, m.Cible.ToString());
        var cle = $"virement-{Contexte.Cle}";

        // ① Débiter la source. Si une incarnation précédente a planté après cette étape, on ne la refait pas
        //    (et même refaite, la clé la rendrait sans effet côté compte).
        if (_etat.Statut == StatutVirement.EnCours)
        {
            if (await source.Demander(new Debiter(cle, m.Montant, $"Virement {Contexte.Cle} émis")) is Refus refus)
                return await Finir(StatutVirement.Refuse, refus.Motif);
            _etat.Statut = StatutVirement.DebitFait;
            await Sauver();
        }

        await pieges.Verifier(Contexte.Chemin, Etape.ApresDebit);

        // ② Créditer la cible ; si elle refuse (compte inexistant…), ③ rendre l'argent à la source : une compensation,
        //    comme dans la saga de la démo 14, mais ici chaque participant est un acteur.
        if (await cible.Demander(new Crediter(cle, m.Montant, $"Virement {Contexte.Cle} reçu")) is Refus refusCible)
        {
            await source.Demander(new Crediter($"{cle}-annulation", m.Montant, $"Annulation du virement {Contexte.Cle}"));
            return await Finir(StatutVirement.Annule, refusCible.Motif);
        }
        return await Finir(StatutVirement.Termine, null);
    }

    private async Task<object> Finir(StatutVirement statut, string? motif)
    {
        _etat!.Statut = statut;
        _etat.Motif = motif;
        await Sauver();
        return Vue();
    }

    private Task Sauver() => magasin.Ecrire(Contexte.Chemin, _etat!);

    private VirementDto Vue() => new(Contexte.Cle, _etat!.Source, _etat.Cible, _etat.Montant, _etat.Statut, _etat.Motif);
}

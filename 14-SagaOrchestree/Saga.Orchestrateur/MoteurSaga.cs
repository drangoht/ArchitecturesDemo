using Saga.Participants;

namespace Saga.Orchestrateur;

/// <summary>
/// Le moteur d'orchestration. À chaque appel de Traiter(), il fait avancer d'UN pas chaque saga prête,
/// et enregistre son nouvel état. Il ne garde rien en mémoire : on peut l'arrêter et le relancer à tout moment.
/// </summary>
public sealed class MoteurSaga(MagasinSagas magasin, DefinitionVirement definition, TimeProvider horloge, OptionsSaga options)
{
    private readonly SemaphoreSlim _verrou = new(1, 1);
    private IReadOnlyList<Etape> Etapes => definition.Etapes;
    private DateTime Maintenant => horloge.GetUtcNow().UtcDateTime;

    public IEnumerable<string> NomsDesEtapes => Etapes.Select(e => e.Nom);

    public EtatSaga Demarrer(Guid source, Guid cible, decimal montant)
    {
        var saga = new EtatSaga { Id = Guid.NewGuid(), Source = source, Cible = cible, Montant = montant, ProchainEssai = Maintenant };
        Noter(saga, $"▶ Virement de {montant:0.00} demandé");
        magasin.Enregistrer(saga);
        return saga;
    }

    /// <summary>Fait avancer d'un pas toutes les sagas prêtes. Renvoie le nombre de sagas traitées.</summary>
    public async Task<int> Traiter()
    {
        await _verrou.WaitAsync();
        try
        {
            var pretes = magasin.Toutes().Where(EstPrete).ToList();
            foreach (var saga in pretes)
            {
                await Avancer(saga);
                magasin.Enregistrer(saga);  // l'état est enregistré après CHAQUE pas
            }
            return pretes.Count;
        }
        finally
        {
            _verrou.Release();
        }
    }

    /// <summary>Événement extérieur : la décision de l'analyste de conformité.</summary>
    public async Task<bool> Signaler(Guid id, bool approuve, string? commentaire)
    {
        await _verrou.WaitAsync();
        try
        {
            var saga = magasin.Charger(id);
            if (saga is not { Statut: StatutSaga.EnAttente })
                return false;

            if (approuve)
            {
                Noter(saga, $"✉ Décision reçue : approuvé{Commentaire(commentaire)}");
                TerminerEtape(saga, Etapes[saga.EtapeCourante]);
            }
            else
            {
                DemarrerCompensation(saga, $"Refusé par l'analyste de conformité{Commentaire(commentaire)}");
            }
            magasin.Enregistrer(saga);
            return true;
        }
        finally
        {
            _verrou.Release();
        }
    }

    private bool EstPrete(EtatSaga saga) => saga.Statut switch
    {
        StatutSaga.EnAttente => saga.Echeance <= Maintenant,  // seul un dépassement d'échéance le réveille
        _ => !saga.EstFinie && saga.ProchainEssai <= Maintenant
    };

    private Task Avancer(EtatSaga saga)
    {
        switch (saga.Statut)
        {
            case StatutSaga.EnAttente:
                DemarrerCompensation(saga, $"Aucune décision avant l'échéance pour {Etapes[saga.EtapeCourante].Nom}");
                return Task.CompletedTask;
            case StatutSaga.Compensation:
                return CompenserUnPas(saga);
            default:
                return ExecuterEtapeCourante(saga);
        }
    }

    private async Task ExecuterEtapeCourante(EtatSaga saga)
    {
        if (saga.EtapeCourante >= Etapes.Count)
        {
            saga.Statut = StatutSaga.Terminee;
            Noter(saga, "■ Virement terminé");
            return;
        }

        var etape = Etapes[saga.EtapeCourante];
        Issue issue;
        try
        {
            issue = await etape.Executer(saga);
        }
        catch (RefusMetierException e)
        {
            issue = new Issue.Refusee(e.Message);
        }
        catch (Exception e)
        {
            EchecPassager(saga, etape, e.Message);
            return;
        }

        switch (issue)
        {
            case Issue.Reussie:
                TerminerEtape(saga, etape);
                break;
            case Issue.EnAttente attente:
                saga.Statut = StatutSaga.EnAttente;
                saga.Echeance = Maintenant + attente.Delai;
                Noter(saga, $"⏳ {etape.Nom} : {attente.Motif} (échéance {saga.Echeance:HH:mm:ss} UTC)");
                break;
            case Issue.Refusee refus when PointDeNonRetourPasse(saga):
                ATraiterManuellement(saga, $"{etape.Nom} refusé après le point de non-retour : {refus.Raison}");
                break;
            case Issue.Refusee refus:
                DemarrerCompensation(saga, $"{etape.Nom} refusé : {refus.Raison}");
                break;
        }
    }

    private void EchecPassager(EtatSaga saga, Etape etape, string message)
    {
        saga.Tentatives++;
        if (saga.Tentatives < options.TentativesMax)
        {
            var attente = Attente(saga.Tentatives);
            saga.ProchainEssai = Maintenant + attente;
            Noter(saga, $"✖ {etape.Nom} : {message} — tentative {saga.Tentatives}/{options.TentativesMax}, " +
                        $"nouvel essai dans {attente.TotalMilliseconds:0} ms");
        }
        else if (PointDeNonRetourPasse(saga))
        {
            // On ne peut plus défaire : l'argent a déjà été débité définitivement.
            ATraiterManuellement(saga, $"{etape.Nom} impossible après {options.TentativesMax} tentatives : {message}");
        }
        else
        {
            DemarrerCompensation(saga, $"{etape.Nom} impossible après {options.TentativesMax} tentatives : {message}");
        }
    }

    /// <summary>Défait la plus récente des étapes réussies. Une compensation doit finir par réussir : on réessaie sans fin.</summary>
    private async Task CompenserUnPas(EtatSaga saga)
    {
        if (saga.EtapesFaites.Count == 0)
        {
            saga.Statut = StatutSaga.Annulee;
            Noter(saga, "■ Virement annulé : toutes les étapes ont été défaites");
            return;
        }

        var etape = Etapes.Single(e => e.Nom == saga.EtapesFaites[^1]);
        if (etape.Compenser is not null)
        {
            try
            {
                await etape.Compenser(saga);
                Noter(saga, $"↩ {etape.Nom} défaite");
            }
            catch (Exception e)
            {
                saga.Tentatives++;
                saga.ProchainEssai = Maintenant + Attente(Math.Min(saga.Tentatives, 6));
                Noter(saga, $"✖ Compensation de {etape.Nom} : {e.Message} — on réessaiera");
                return;
            }
        }
        saga.EtapesFaites.RemoveAt(saga.EtapesFaites.Count - 1);
        saga.Tentatives = 0;
        saga.ProchainEssai = Maintenant;
    }

    private void TerminerEtape(EtatSaga saga, Etape etape)
    {
        saga.EtapesFaites.Add(etape.Nom);
        saga.EtapeCourante++;
        (saga.Statut, saga.Tentatives, saga.Echeance) = (StatutSaga.EnCours, 0, null);
        saga.ProchainEssai = Maintenant.AddMilliseconds(options.PauseEntreEtapesMs);
        Noter(saga, $"✔ {etape.Nom}{(etape.PointDeNonRetour ? " (point de non-retour franchi)" : "")}");
    }

    private void DemarrerCompensation(EtatSaga saga, string raison)
    {
        (saga.Statut, saga.Raison, saga.Tentatives, saga.Echeance, saga.ProchainEssai) =
            (StatutSaga.Compensation, raison, 0, null, Maintenant);
        Noter(saga, $"↩ Compensation : {raison}");
    }

    private void ATraiterManuellement(EtatSaga saga, string raison)
    {
        (saga.Statut, saga.Raison) = (StatutSaga.ATraiterManuellement, raison);
        Noter(saga, $"⚠ {raison} — intervention manuelle requise");
    }

    private bool PointDeNonRetourPasse(EtatSaga saga) =>
        Etapes.Any(e => e.PointDeNonRetour && saga.EtapesFaites.Contains(e.Nom));

    private TimeSpan Attente(int tentative) => TimeSpan.FromMilliseconds(options.AttenteInitialeMs * Math.Pow(2, tentative - 1));

    private void Noter(EtatSaga saga, string message) => saga.Journal.Add(new EntreeJournal(Maintenant, message));

    private static string Commentaire(string? commentaire) => string.IsNullOrWhiteSpace(commentaire) ? "" : $" ({commentaire})";
}

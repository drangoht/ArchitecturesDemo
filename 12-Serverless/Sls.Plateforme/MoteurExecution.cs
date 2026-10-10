using System.Diagnostics;

namespace Sls.Plateforme;

public record ResultatInvocation(object? Valeur, bool DemarrageAFroid, string Instance, long DureeMs);

public record StatistiquesFonction(
    string Nom, string Declencheur, int MemoireMo, long Invocations, long Erreurs, long DemarragesAFroid,
    int InstancesActives, int PicInstances, double DureeMoyenneMs, double GoSecondes);

/// <summary>
/// Le cœur du fournisseur : il exécute chaque invocation dans une INSTANCE (un conteneur éphémère).
/// - Une instance libre et « chaude » est réutilisée ; sinon on en crée une : c'est le démarrage à froid.
/// - Une instance ne sert qu'une invocation à la fois : 10 requêtes simultanées = 10 instances (mise à l'échelle).
/// - Une instance inactive est recyclée : sans trafic, il ne reste rien qui tourne (mise à zéro).
/// - On facture le temps d'exécution × la mémoire allouée, à la milliseconde.
/// </summary>
public sealed class MoteurExecution
{
    // Tarifs publics d'AWS Lambda (x86) : 0,20 $ par million de requêtes et 0,0000166667 $ par Go-seconde.
    public const decimal PrixParMillionDeRequetes = 0.20m;
    public const decimal PrixParGoSeconde = 0.0000166667m;

    private sealed class Instance(string id)
    {
        public string Id { get; } = id;
        public bool Occupee { get; set; }
        public DateTime DerniereUtilisation { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Memoire { get; } = [];
    }

    private sealed class EtatFonction(DefinitionFonction definition)
    {
        public DefinitionFonction Definition { get; } = definition;
        public List<Instance> Instances { get; } = [];
        public int NumeroInstance;
        public long Invocations, Erreurs, DemarragesAFroid;
        public double DureeTotaleMs, GoSecondes;
        public int PicInstances;
    }

    private readonly Dictionary<string, EtatFonction> _fonctions;
    private readonly ServicesGeres _services;
    private readonly OptionsPlateforme _options;

    public MoteurExecution(Manifeste manifeste, ServicesGeres services, OptionsPlateforme options)
    {
        _fonctions = manifeste.Fonctions.ToDictionary(f => f.Nom, f => new EtatFonction(f));
        _services = services;
        _options = options;
        foreach (var fonction in manifeste.Fonctions.Where(f => f.Type == TypeDeclencheur.Stockage))
            services.Stockage.Abonner(fonction.Prefixe!, fonction.File!);
    }

    public IEnumerable<DefinitionFonction> Definitions => _fonctions.Values.Select(e => e.Definition);

    public bool PeutDemarrer(string nom)
    {
        var etat = _fonctions[nom];
        lock (etat)
            return etat.Instances.Any(i => !i.Occupee) || etat.Instances.Count < _options.InstancesMaxParFonction;
    }

    public async Task<ResultatInvocation> Invoquer(string nom, object declenchement)
    {
        var etat = _fonctions[nom];
        var definition = etat.Definition;

        Instance? instance;
        bool aFroid;
        lock (etat)
        {
            instance = etat.Instances.FirstOrDefault(i => !i.Occupee);
            aFroid = instance is null;
            if (instance is null)
            {
                if (etat.Instances.Count >= _options.InstancesMaxParFonction)
                    throw new EtranglementException(nom);
                instance = new Instance($"{++etat.NumeroInstance}");
                etat.Instances.Add(instance);
                etat.PicInstances = Math.Max(etat.PicInstances, etat.Instances.Count);
            }
            instance.Occupee = true;
        }

        var chrono = Stopwatch.StartNew();
        var (succes, tuee) = (false, false);
        try
        {
            if (aFroid)
            {
                // Téléchargement du code, démarrage du runtime .NET, initialisation : c'est le « cold start ».
                _services.Journal.Ecrire($"{nom}#{instance.Id}", "démarrage à froid d'une nouvelle instance");
                await Task.Delay(_options.DemarrageAFroidMs);
            }

            using var annulation = new CancellationTokenSource(definition.Delai);
            var contexte = new ContexteFonction(nom, instance.Id, aFroid, instance.Memoire, _services, annulation.Token);
            var execution = definition.Executer(declenchement, contexte);
            if (await Task.WhenAny(execution, Task.Delay(definition.Delai)) != execution)
            {
                tuee = true;
                throw new DelaiDepasseException(nom, definition.Delai);
            }

            var valeur = await execution;
            succes = true;
            return new ResultatInvocation(valeur, aFroid, instance.Id, chrono.ElapsedMilliseconds);
        }
        finally
        {
            var dureeMs = Math.Max(1, chrono.Elapsed.TotalMilliseconds);  // facturé à la ms, démarrage à froid compris
            lock (etat)
            {
                etat.Invocations++;
                etat.DemarragesAFroid += aFroid ? 1 : 0;
                etat.Erreurs += succes ? 0 : 1;
                etat.DureeTotaleMs += dureeMs;
                etat.GoSecondes += definition.MemoireMo / 1024.0 * dureeMs / 1000.0;

                if (tuee)
                    etat.Instances.Remove(instance);  // une instance qui dépasse son délai est détruite
                else
                    (instance.Occupee, instance.DerniereUtilisation) = (false, DateTime.UtcNow);
            }
        }
    }

    /// <summary>Supprime les instances inactives depuis trop longtemps : la fonction redescend à zéro.</summary>
    public void Recycler()
    {
        var limite = DateTime.UtcNow.AddMilliseconds(-_options.MsAvantRecyclage);
        foreach (var etat in _fonctions.Values)
        {
            lock (etat)
            {
                foreach (var instance in etat.Instances.Where(i => !i.Occupee && i.DerniereUtilisation < limite).ToList())
                {
                    etat.Instances.Remove(instance);
                    _services.Journal.Ecrire($"{etat.Definition.Nom}#{instance.Id}", "instance inactive recyclée");
                }
            }
        }
    }

    public IReadOnlyList<StatistiquesFonction> Statistiques() => _fonctions.Values.Select(e =>
    {
        lock (e)
            return new StatistiquesFonction(e.Definition.Nom, e.Definition.Declencheur, e.Definition.MemoireMo,
                e.Invocations, e.Erreurs, e.DemarragesAFroid, e.Instances.Count, e.PicInstances,
                e.Invocations == 0 ? 0 : Math.Round(e.DureeTotaleMs / e.Invocations, 1), Math.Round(e.GoSecondes, 4));
    }).ToList();

    public object Facture()
    {
        var statistiques = Statistiques();
        var invocations = statistiques.Sum(s => s.Invocations);
        var goSecondes = statistiques.Sum(s => s.GoSecondes);
        var coutRequetes = invocations / 1_000_000m * PrixParMillionDeRequetes;
        var coutCalcul = (decimal)goSecondes * PrixParGoSeconde;
        return new
        {
            invocations,
            goSecondes = Math.Round(goSecondes, 4),
            coutRequetesUsd = Math.Round(coutRequetes, 8),
            coutCalculUsd = Math.Round(coutCalcul, 8),
            totalUsd = Math.Round(coutRequetes + coutCalcul, 8),
            remarque = "Tarifs AWS Lambda, hors offre gratuite (1 million de requêtes et 400 000 Go-s par mois). " +
                       "Rien n'est facturé quand aucune fonction ne tourne."
        };
    }
}

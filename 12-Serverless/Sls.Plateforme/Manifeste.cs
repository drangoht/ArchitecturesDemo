namespace Sls.Plateforme;

public enum TypeDeclencheur { Http, File, Minuterie, Stockage }

public sealed record DefinitionFonction(
    string Nom,
    TypeDeclencheur Type,
    string Declencheur,
    int MemoireMo,
    TimeSpan Delai,
    Func<object, ContexteFonction, Task<object?>> Executer)
{
    public string? Methode { get; init; }
    public string? Route { get; init; }
    public string? File { get; init; }
    public string? Prefixe { get; init; }
    public TimeSpan? Periode { get; init; }
}

/// <summary>
/// Le « manifeste de déploiement » : la liste des fonctions et de ce qui les déclenche.
/// C'est l'équivalent de serverless.yml, template.yaml (AWS SAM) ou des attributs [Function] d'Azure.
/// La mémoire allouée et le délai maximal se règlent par fonction, et déterminent le prix.
/// </summary>
public sealed class Manifeste
{
    internal List<DefinitionFonction> Fonctions { get; } = [];

    public Manifeste Http(string nom, string methode, string route,
        Func<RequeteHttp, ContexteFonction, Task<ReponseHttp>> fonction, int memoireMo = 128, double delaiSecondes = 10) =>
        Ajouter(new DefinitionFonction(nom, TypeDeclencheur.Http, $"HTTP {methode} {route}", memoireMo,
            TimeSpan.FromSeconds(delaiSecondes), async (d, c) => await fonction((RequeteHttp)d, c))
        {
            Methode = methode, Route = route
        });

    public Manifeste File(string nom, string file,
        Func<MessageFile, ContexteFonction, Task> fonction, int memoireMo = 128, double delaiSecondes = 30) =>
        Ajouter(new DefinitionFonction(nom, TypeDeclencheur.File, $"File « {file} »", memoireMo,
            TimeSpan.FromSeconds(delaiSecondes), async (d, c) => { await fonction((MessageFile)d, c); return null; })
        {
            File = file
        });

    public Manifeste Minuterie(string nom, TimeSpan periode,
        Func<TicMinuterie, ContexteFonction, Task> fonction, int memoireMo = 128, double delaiSecondes = 30) =>
        Ajouter(new DefinitionFonction(nom, TypeDeclencheur.Minuterie, $"Toutes les {periode.TotalSeconds:0} s", memoireMo,
            TimeSpan.FromSeconds(delaiSecondes), async (d, c) => { await fonction((TicMinuterie)d, c); return null; })
        {
            Periode = periode
        });

    /// <summary>Déclenchée à chaque objet écrit sous un préfixe (en interne : via une file, pour bénéficier des nouveaux essais).</summary>
    public Manifeste Stockage(string nom, string prefixe,
        Func<ObjetStocke, ContexteFonction, Task> fonction, int memoireMo = 128, double delaiSecondes = 30) =>
        Ajouter(new DefinitionFonction(nom, TypeDeclencheur.Stockage, $"Stockage « {prefixe}* »", memoireMo,
            TimeSpan.FromSeconds(delaiSecondes), async (d, c) => { await fonction((ObjetStocke)d, c); return null; })
        {
            Prefixe = prefixe, File = $"$stockage/{nom}"
        });

    private Manifeste Ajouter(DefinitionFonction definition)
    {
        Fonctions.Add(definition);
        return this;
    }
}

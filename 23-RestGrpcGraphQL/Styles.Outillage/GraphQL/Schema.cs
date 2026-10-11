using System.Globalization;
using System.Text;

namespace Styles.Outillage.GraphQL;

/// <summary>Le parent des champs racines (Query, Mutation) : il n'y a rien au-dessus.</summary>
public sealed class Racine
{
    public static readonly Racine Instance = new();
}

/// <summary>Un argument de champ : son nom, son type, et sa valeur par défaut éventuelle (int, double, string, bool).</summary>
public sealed record Arg(string Nom, string Type, object? ParDefaut = null)
{
    internal RefType RefType { get; } = RefType.Analyser(Type);
}

/// <summary>Les arguments d'un champ, déjà convertis selon leur type déclaré.</summary>
public sealed class Arguments(IReadOnlyDictionary<string, object?> valeurs)
{
    public static readonly Arguments Aucun = new(new Dictionary<string, object?>());

    public T Lire<T>(string nom) => (T)valeurs[nom]!;

    public T? LireOuDefaut<T>(string nom) => valeurs.GetValueOrDefault(nom) is T valeur ? valeur : default;
}

/// <summary>
/// Un champ et la façon de le résoudre : soit objet par objet, soit pour TOUS les objets d'un même niveau à la fois
/// (résolveur groupé, le « DataLoader » : un seul aller-retour vers la base au lieu d'un par objet).
/// </summary>
public sealed class ChampGraphQL(string nom, RefType type, IReadOnlyList<Arg> args,
    Func<IReadOnlyList<object>, Arguments, IReadOnlyList<object?>> resoudre, bool groupe)
{
    public string Nom => nom;
    public RefType Type => type;
    public IReadOnlyList<Arg> Args => args;
    public bool Groupe => groupe;
    public string? RaisonDepreciation { get; internal set; }

    internal IReadOnlyList<object?> Resoudre(IReadOnlyList<object> parents, Arguments arguments) => resoudre(parents, arguments);
}

public abstract class TypeGraphQL(string nom)
{
    public string Nom => nom;
}

public sealed class TypeScalaire(string nom) : TypeGraphQL(nom);

public sealed class TypeEnum(string nom, IReadOnlyList<string> valeurs) : TypeGraphQL(nom)
{
    public IReadOnlyList<string> Valeurs => valeurs;
}

public class TypeObjet(string nom) : TypeGraphQL(nom)
{
    private readonly List<ChampGraphQL> _champs = [];

    public IReadOnlyList<ChampGraphQL> Champs => _champs;

    public ChampGraphQL? Champ(string nom) => _champs.Find(c => c.Nom == nom);

    protected void Ajouter(ChampGraphQL champ)
    {
        if (Champ(champ.Nom) is not null)
            throw new InvalidOperationException($"Champ {Nom}.{champ.Nom} déclaré deux fois.");
        _champs.Add(champ);
    }

    internal void Deprecier(string raison) => _champs[^1].RaisonDepreciation = raison;
}

/// <summary>Un type objet dont les résolveurs reçoivent des <typeparamref name="T"/> : la définition reste typée.</summary>
public sealed class TypeObjet<T>(string nom) : TypeObjet(nom) where T : class
{
    /// <summary>Un champ résolu objet par objet.</summary>
    public TypeObjet<T> Champ(string nom, string type, Func<T, object?> resoudre) =>
        Champ(nom, type, (parent, _) => resoudre(parent));

    public TypeObjet<T> Champ(string nom, string type, Func<T, Arguments, object?> resoudre, params Arg[] args)
    {
        Ajouter(new ChampGraphQL(nom, RefType.Analyser(type), args,
            (parents, arguments) => parents.Select(p => ResoudreUn(p, arguments)).ToList(), groupe: false));
        return this;

        object? ResoudreUn(object parent, Arguments arguments)
        {
            try
            {
                return resoudre((T)parent, arguments);
            }
            catch (Exception e)   // l'échec ne concerne que ce parent : les autres sont servis
            {
                return new EchecResolution(e);
            }
        }
    }

    /// <summary>Un champ résolu pour tous les parents d'un niveau à la fois ; la liste rendue suit l'ordre des parents.</summary>
    public TypeObjet<T> ChampGroupe(string nom, string type, Func<IReadOnlyList<T>, Arguments, IReadOnlyList<object?>> resoudre, params Arg[] args)
    {
        Ajouter(new ChampGraphQL(nom, RefType.Analyser(type), args,
            (parents, arguments) => resoudre(parents.Cast<T>().ToList(), arguments), groupe: true));
        return this;
    }

    /// <summary>Marque le dernier champ déclaré comme déprécié : il reste servi, mais le schéma le signale.</summary>
    public TypeObjet<T> Deprecie(string raison)
    {
        Deprecier(raison);
        return this;
    }
}

/// <summary>Le schéma : le contrat de l'API GraphQL, publié tel quel (SDL) et vérifié à chaque requête.</summary>
public sealed class Schema
{
    private readonly Dictionary<string, TypeGraphQL> _types = new()
    {
        ["ID"] = new TypeScalaire("ID"),
        ["String"] = new TypeScalaire("String"),
        ["Int"] = new TypeScalaire("Int"),
        ["Float"] = new TypeScalaire("Float"),
        ["Boolean"] = new TypeScalaire("Boolean"),
    };
    private readonly List<TypeGraphQL> _declares = [];

    public TypeObjet<Racine> Query => (TypeObjet<Racine>)(_types.GetValueOrDefault("Query") ?? Objet<Racine>("Query"));

    public TypeObjet<Racine>? Mutation => _types.GetValueOrDefault("Mutation") as TypeObjet<Racine>;

    public TypeObjet<T> Objet<T>(string nom) where T : class => Declarer(new TypeObjet<T>(nom));

    public TypeObjet<Racine> DeclarerMutation() => Objet<Racine>("Mutation");

    public Schema Enum(string nom, params string[] valeurs)
    {
        Declarer(new TypeEnum(nom, valeurs));
        return this;
    }

    public TypeGraphQL? Type(string nom) => _types.GetValueOrDefault(nom);

    private T Declarer<T>(T type) where T : TypeGraphQL
    {
        if (!_types.TryAdd(type.Nom, type))
            throw new InvalidOperationException($"Type {type.Nom} déclaré deux fois.");
        _declares.Add(type);
        return type;
    }

    /// <summary>Vérifie que chaque type cité existe : une faute de frappe dans le schéma doit se voir au démarrage.</summary>
    public void Verifier()
    {
        foreach (var objet in _declares.OfType<TypeObjet>())
            foreach (var champ in objet.Champs)
                foreach (var nom in champ.Args.Select(a => a.RefType.NomNu).Prepend(champ.Type.NomNu))
                    if (!_types.ContainsKey(nom))
                        throw new InvalidOperationException($"{objet.Nom}.{champ.Nom} : type « {nom} » inconnu.");
    }

    /// <summary>Le schéma en SDL (Schema Definition Language) : ce que publie un serveur GraphQL pour ses clients.</summary>
    public string EnSdl()
    {
        var sdl = new StringBuilder();
        foreach (var type in _declares)
        {
            switch (type)
            {
                case TypeEnum e:
                    sdl.Append("enum ").Append(e.Nom).AppendLine(" {");
                    foreach (var valeur in e.Valeurs)
                        sdl.Append("  ").AppendLine(valeur);
                    break;
                case TypeObjet o:
                    sdl.Append("type ").Append(o.Nom).AppendLine(" {");
                    foreach (var champ in o.Champs)
                    {
                        sdl.Append("  ").Append(champ.Nom);
                        if (champ.Args.Count > 0)
                            sdl.Append('(').AppendJoin(", ", champ.Args.Select(a =>
                                $"{a.Nom}: {a.Type}" + (a.ParDefaut is null ? "" : $" = {Litteral(a.ParDefaut)}"))).Append(')');
                        sdl.Append(": ").Append(champ.Type);
                        if (champ.RaisonDepreciation is { } raison)
                            sdl.Append(" @deprecated(reason: ").Append(Litteral(raison)).Append(')');
                        sdl.AppendLine();
                    }
                    break;
            }
            sdl.AppendLine("}").AppendLine();
        }
        return sdl.ToString().TrimEnd() + "\n";
    }

    private static string Litteral(object valeur) => valeur switch
    {
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => valeur.ToString()!
    };
}

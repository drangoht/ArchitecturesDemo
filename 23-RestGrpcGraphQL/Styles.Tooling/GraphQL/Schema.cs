using System.Globalization;
using System.Text;

namespace Styles.Tooling.GraphQL;

/// <summary>The parent of the root fields (Query, Mutation): there is nothing above.</summary>
public sealed class Root
{
    public static readonly Root Instance = new();
}

/// <summary>A field argument: its name, its type, and its possible default value (int, double, string, bool).</summary>
public sealed record Arg(string Name, string Type, object? Default = null)
{
    internal TypeRef TypeRef { get; } = TypeRef.Parse(Type);
}

/// <summary>The arguments of a field, already converted according to their declared type.</summary>
public sealed class Arguments(IReadOnlyDictionary<string, object?> values)
{
    public static readonly Arguments None = new(new Dictionary<string, object?>());

    public T Get<T>(string name) => (T)values[name]!;

    public T? GetOrDefault<T>(string name) => values.GetValueOrDefault(name) is T value ? value : default;
}

/// <summary>
/// A field and the way to resolve it: either object by object, or for ALL the objects of the same level at once
/// (batched resolver, the "DataLoader": a single round trip to the database instead of one per object).
/// </summary>
public sealed class GraphQLField(string name, TypeRef type, IReadOnlyList<Arg> args,
    Func<IReadOnlyList<object>, Arguments, IReadOnlyList<object?>> resolve, bool batched)
{
    public string Name => name;
    public TypeRef Type => type;
    public IReadOnlyList<Arg> Args => args;
    public bool Batched => batched;
    public string? DeprecationReason { get; internal set; }

    internal IReadOnlyList<object?> Resolve(IReadOnlyList<object> parents, Arguments arguments) => resolve(parents, arguments);
}

public abstract class GraphQLType(string name)
{
    public string Name => name;
}

public sealed class ScalarType(string name) : GraphQLType(name);

public sealed class EnumType(string name, IReadOnlyList<string> values) : GraphQLType(name)
{
    public IReadOnlyList<string> Values => values;
}

public class ObjectType(string name) : GraphQLType(name)
{
    private readonly List<GraphQLField> _fields = [];

    public IReadOnlyList<GraphQLField> Fields => _fields;

    public GraphQLField? Field(string name) => _fields.Find(c => c.Name == name);

    protected void Add(GraphQLField field)
    {
        if (Field(field.Name) is not null)
            throw new InvalidOperationException($"Field {Name}.{field.Name} declared twice.");
        _fields.Add(field);
    }

    internal void Deprecate(string reason) => _fields[^1].DeprecationReason = reason;
}

/// <summary>An object type whose resolvers receive <typeparamref name="T"/> objects: the definition stays typed.</summary>
public sealed class ObjectType<T>(string name) : ObjectType(name) where T : class
{
    /// <summary>A field resolved object by object.</summary>
    public ObjectType<T> Field(string name, string type, Func<T, object?> resolve) =>
        Field(name, type, (parent, _) => resolve(parent));

    public ObjectType<T> Field(string name, string type, Func<T, Arguments, object?> resolve, params Arg[] args)
    {
        Add(new GraphQLField(name, TypeRef.Parse(type), args,
            (parents, arguments) => parents.Select(p => ResolveOne(p, arguments)).ToList(), batched: false));
        return this;

        object? ResolveOne(object parent, Arguments arguments)
        {
            try
            {
                return resolve((T)parent, arguments);
            }
            catch (Exception e)   // the failure only concerns this parent: the others are served
            {
                return new ResolutionFailure(e);
            }
        }
    }

    /// <summary>A field resolved for all the parents of a level at once; the returned list follows the parents' order.</summary>
    public ObjectType<T> BatchedField(string name, string type, Func<IReadOnlyList<T>, Arguments, IReadOnlyList<object?>> resolve, params Arg[] args)
    {
        Add(new GraphQLField(name, TypeRef.Parse(type), args,
            (parents, arguments) => resolve(parents.Cast<T>().ToList(), arguments), batched: true));
        return this;
    }

    /// <summary>Marks the last declared field as deprecated: it is still served, but the schema flags it.</summary>
    public ObjectType<T> Deprecated(string reason)
    {
        Deprecate(reason);
        return this;
    }
}

/// <summary>The schema: the contract of the GraphQL API, published as is (SDL) and checked at each query.</summary>
public sealed class Schema
{
    private readonly Dictionary<string, GraphQLType> _types = new()
    {
        ["ID"] = new ScalarType("ID"),
        ["String"] = new ScalarType("String"),
        ["Int"] = new ScalarType("Int"),
        ["Float"] = new ScalarType("Float"),
        ["Boolean"] = new ScalarType("Boolean"),
    };
    private readonly List<GraphQLType> _declared = [];

    public ObjectType<Root> Query => (ObjectType<Root>)(_types.GetValueOrDefault("Query") ?? Object<Root>("Query"));

    public ObjectType<Root>? Mutation => _types.GetValueOrDefault("Mutation") as ObjectType<Root>;

    public ObjectType<T> Object<T>(string name) where T : class => Declare(new ObjectType<T>(name));

    public ObjectType<Root> DeclareMutation() => Object<Root>("Mutation");

    public Schema Enum(string name, params string[] values)
    {
        Declare(new EnumType(name, values));
        return this;
    }

    public GraphQLType? Type(string name) => _types.GetValueOrDefault(name);

    private T Declare<T>(T type) where T : GraphQLType
    {
        if (!_types.TryAdd(type.Name, type))
            throw new InvalidOperationException($"Type {type.Name} declared twice.");
        _declared.Add(type);
        return type;
    }

    /// <summary>Checks that each type mentioned exists: a typo in the schema must show at startup.</summary>
    public void Check()
    {
        foreach (var obj in _declared.OfType<ObjectType>())
            foreach (var field in obj.Fields)
                foreach (var name in field.Args.Select(a => a.TypeRef.BareName).Prepend(field.Type.BareName))
                    if (!_types.ContainsKey(name))
                        throw new InvalidOperationException($"{obj.Name}.{field.Name}: unknown type \"{name}\".");
    }

    /// <summary>The schema in SDL (Schema Definition Language): what a GraphQL server publishes for its clients.</summary>
    public string ToSdl()
    {
        var sdl = new StringBuilder();
        foreach (var type in _declared)
        {
            switch (type)
            {
                case EnumType e:
                    sdl.Append("enum ").Append(e.Name).AppendLine(" {");
                    foreach (var value in e.Values)
                        sdl.Append("  ").AppendLine(value);
                    break;
                case ObjectType o:
                    sdl.Append("type ").Append(o.Name).AppendLine(" {");
                    foreach (var field in o.Fields)
                    {
                        sdl.Append("  ").Append(field.Name);
                        if (field.Args.Count > 0)
                            sdl.Append('(').AppendJoin(", ", field.Args.Select(a =>
                                $"{a.Name}: {a.Type}" + (a.Default is null ? "" : $" = {Literal(a.Default)}"))).Append(')');
                        sdl.Append(": ").Append(field.Type);
                        if (field.DeprecationReason is { } reason)
                            sdl.Append(" @deprecated(reason: ").Append(Literal(reason)).Append(')');
                        sdl.AppendLine();
                    }
                    break;
            }
            sdl.AppendLine("}").AppendLine();
        }
        return sdl.ToString().TrimEnd() + "\n";
    }

    private static string Literal(object value) => value switch
    {
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!
    };
}

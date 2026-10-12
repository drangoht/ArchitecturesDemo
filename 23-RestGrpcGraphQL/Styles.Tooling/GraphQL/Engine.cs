using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Styles.Tooling.GraphQL;

/// <summary>An error to show as is to the client, with its code (in "extensions.code").</summary>
public sealed class GraphQLError(string message, string code) : Exception(message)
{
    public string Code => code;
}

public sealed class GraphQLOptions
{
    /// <summary>
    /// The maximum number of nested selection levels ("{ customer { name } }" has two). The client chooses its query: without a limit, "customer → accounts → owner →
    /// accounts → …" would make the server work endlessly (in production: also a maximum cost per query).
    /// </summary>
    public int MaxDepth { get; init; } = 6;

    /// <summary>Translates a resolver's exception into a showable error; otherwise, "Internal error." without detail.</summary>
    public Func<Exception, GraphQLError?>? TranslateError { get; init; }
}

/// <summary>What a resolver that failed for a parent returns: the field will be null and an error will be added.</summary>
internal sealed class ResolutionFailure(Exception exception)
{
    public Exception Exception => exception;
}

/// <summary>
/// The engine: parsing, validation against the schema, conversion of variables and arguments, then execution.
/// Execution is done by LEVELS: a field is resolved for all the objects of the same level at once, which allows
/// the batched resolvers to do a single read where one per object would be needed (the "N+1" problem).
/// </summary>
public sealed class GraphQLEngine
{
    private readonly Schema _schema;
    private readonly GraphQLOptions _options;
    private readonly ConcurrentDictionary<string, long> _usage = new();

    public GraphQLEngine(Schema schema, GraphQLOptions? options = null)
    {
        schema.Check();
        _schema = schema;
        _options = options ?? new GraphQLOptions();
    }

    public Schema Schema => _schema;

    /// <summary>
    /// How many queries asked for each field. The GraphQL server knows exactly what its clients read:
    /// we know when nobody uses a deprecated field any more, and it can then be removed.
    /// </summary>
    public IReadOnlyDictionary<string, long> Usage =>
        new SortedDictionary<string, long>(_usage.ToDictionary(), StringComparer.Ordinal);

    public JsonObject Execute(string? query, JsonObject? variables = null, string? operationName = null)
    {
        var errors = new JsonArray();

        IReadOnlyList<OperationAst> operations;
        try
        {
            operations = Parser.ParseDocument(query ?? "");
        }
        catch (SyntaxError e)
        {
            return new JsonObject { ["errors"] = new JsonArray(Error(e.Message, "GRAPHQL_PARSE_FAILED", e.Position)) };
        }

        var operation = operationName is null
            ? operations.Count == 1 ? operations[0] : null
            : operations.FirstOrDefault(o => o.Name == operationName);
        var root = operation?.Type == "mutation" ? _schema.Mutation : _schema.Query;
        if (operation is null || root is null)
        {
            var message = operation is null
                ? operationName is null ? "Several operations in the document: specify \"operationName\"." : $"Operation \"{operationName}\" not found."
                : "This schema has no mutations.";
            return new JsonObject { ["errors"] = new JsonArray(Error(message, "GRAPHQL_VALIDATION_FAILED")) };
        }

        // 1. Validate the query against the schema, BEFORE executing anything.
        var validation = new Validation(_schema, _options, operation);
        validation.Validate(root, operation.Selections, depth: 1);
        validation.CheckVariables();
        if (validation.Errors.Count > 0)
            return new JsonObject { ["errors"] = new JsonArray(validation.Errors.ToArray<JsonNode?>()) };

        // 2. Convert variables and arguments according to the declared types.
        Dictionary<Selection, Arguments> arguments;
        try
        {
            var variableValues = new Inputs(_schema).Variables(operation.Variables, variables);
            arguments = new Dictionary<Selection, Arguments>(ReferenceEqualityComparer.Instance);
            ComputeArguments(root, operation.Selections, variableValues, arguments);
        }
        catch (GraphQLError e)
        {
            return new JsonObject { ["errors"] = new JsonArray(Error(e.Message, e.Code)) };
        }

        // 3. Execute. The resolvers' errors do not stop everything: the other fields are served (partial response).
        var execution = new Execution(this, arguments, errors);
        var data = execution.ExecuteSelection(root, [Root.Instance], [[]], operation.Selections)[0];

        var response = new JsonObject { ["data"] = data };
        if (errors.Count > 0)
            response["errors"] = errors;
        return response;
    }

    private void ComputeArguments(ObjectType type, IReadOnlyList<Selection> selections, IReadOnlyDictionary<string, object?> variables,
        Dictionary<Selection, Arguments> result)
    {
        var inputs = new Inputs(_schema);
        foreach (var selection in selections)
        {
            if (type.Field(selection.Name) is not { } field)
                continue;   // __typename
            var values = new Dictionary<string, object?>();
            foreach (var arg in field.Args)
            {
                var provided = selection.Arguments.FirstOrDefault(a => a.Name == arg.Name);
                if (provided is null)
                {
                    if (arg.Default is not null)
                        values[arg.Name] = arg.Default;
                    continue;
                }
                if (provided.Value is VariableValue v && !variables.ContainsKey(v.Name))
                {
                    if (arg.Default is not null)
                        values[arg.Name] = arg.Default;   // optional variable not provided: the argument keeps its default
                    continue;
                }
                values[arg.Name] = inputs.Literal(provided.Value, arg.TypeRef, variables, $"Argument \"{arg.Name}\" of \"{type.Name}.{field.Name}\"");
            }
            result[selection] = new Arguments(values);
            if (selection.SubSelection is not null && _schema.Type(field.Type.BareName) is ObjectType subType)
                ComputeArguments(subType, selection.SubSelection, variables, result);
        }
    }

    internal static JsonObject Error(string message, string code, Position? position = null, IEnumerable<object>? path = null)
    {
        var error = new JsonObject { ["message"] = message };
        if (position is not null)
            error["locations"] = new JsonArray(new JsonObject { ["line"] = position.Line, ["column"] = position.Column });
        if (path is not null)
            error["path"] = new JsonArray(path.Select(e => e is int i ? JsonValue.Create(i) : (JsonNode)JsonValue.Create((string)e)).ToArray<JsonNode?>());
        error["extensions"] = new JsonObject { ["code"] = code };
        return error;
    }

    // ─── Validation ───

    private sealed class Validation(Schema schema, GraphQLOptions options, OperationAst operation)
    {
        private readonly HashSet<string> _usedVariables = [];
        private bool _tooDeep;

        public List<JsonObject> Errors { get; } = [];

        private void Report(string message, Position position) =>
            Errors.Add(Error(message, "GRAPHQL_VALIDATION_FAILED", position));

        public void Validate(ObjectType type, IReadOnlyList<Selection> selections, int depth)
        {
            if (depth > options.MaxDepth && !_tooDeep)
            {
                _tooDeep = true;
                Report($"The query exceeds the maximum depth of {options.MaxDepth} levels.", selections[0].Position);
            }
            foreach (var duplicate in selections.GroupBy(s => s.Key).Where(g => g.Count() > 1))
                Report($"\"{duplicate.Key}\" appears twice at the same level: use aliases.", duplicate.Last().Position);

            foreach (var selection in selections)
            {
                if (selection.Name == "__typename")
                {
                    if (selection.SubSelection is not null)
                        Report("\"__typename\" has no subfields.", selection.Position);
                    continue;
                }
                if (type.Field(selection.Name) is not { } field)
                {
                    Report($"The field \"{selection.Name}\" does not exist on type \"{type.Name}\".", selection.Position);
                    continue;
                }
                foreach (var argument in selection.Arguments)
                {
                    if (field.Args.FirstOrDefault(a => a.Name == argument.Name) is not { } declared)
                        Report($"The argument \"{argument.Name}\" does not exist on \"{type.Name}.{field.Name}\".", selection.Position);
                    else if (argument.Value is VariableValue v)
                        CheckVariableUsage(v.Name, declared, selection.Position);
                }
                foreach (var missing in field.Args.Where(a => a.TypeRef.NonNull && a.Default is null && selection.Arguments.All(s => s.Name != a.Name)))
                    Report($"The argument \"{missing.Name}\" is required for \"{type.Name}.{field.Name}\".", selection.Position);

                if (schema.Type(field.Type.BareName) is ObjectType subType)
                {
                    if (selection.SubSelection is null)
                        Report($"\"{type.Name}.{field.Name}\" is of type {field.Type}: specify the subfields wanted.", selection.Position);
                    else
                        Validate(subType, selection.SubSelection, depth + 1);
                }
                else if (selection.SubSelection is not null)
                    Report($"\"{type.Name}.{field.Name}\" is of type {field.Type}: it has no subfields.", selection.Position);
            }
        }

        private void CheckVariableUsage(string name, Arg argument, Position position)
        {
            _usedVariables.Add(name);
            if (operation.Variables.FirstOrDefault(d => d.Name == name) is not { } definition)
                Report($"The variable \"${name}\" is not declared.", position);
            else if (definition.Type.BareName != argument.TypeRef.BareName
                     || (argument.TypeRef.NonNull && !definition.Type.NonNull && definition.Default is null))
                Report($"The variable \"${name}\" ({definition.Type}) does not fit the argument \"{argument.Name}\" ({argument.Type}).", position);
        }

        public void CheckVariables()
        {
            foreach (var definition in operation.Variables)
            {
                if (!_usedVariables.Contains(definition.Name))
                    Report($"The variable \"${definition.Name}\" is declared but not used.", definition.Position);
                if (schema.Type(definition.Type.BareName) is null or ObjectType || definition.Type.Element is not null)
                    Report($"The variable \"${definition.Name}\": type {definition.Type} not accepted as input.", definition.Position);
            }
        }
    }

    // ─── Execution ───

    private sealed class Execution(GraphQLEngine engine, Dictionary<Selection, Arguments> arguments, JsonArray errors)
    {
        private readonly record struct Result(bool Invalid, JsonNode? Node);

        /// <summary>
        /// Resolves a selection for all the parents of a level. Returns, for each parent, its JSON object, or null if
        /// a non-null field could not be obtained (the whole object then becomes null: null propagation).
        /// </summary>
        public List<JsonObject?> ExecuteSelection(ObjectType type, IReadOnlyList<object> parents, IReadOnlyList<object[]> paths,
            IReadOnlyList<Selection> selections)
        {
            var results = parents.Select(_ => (JsonObject?)new JsonObject()).ToList();
            foreach (var selection in selections)
            {
                if (selection.Name == "__typename")
                {
                    foreach (var obj in results.OfType<JsonObject>())
                        obj[selection.Key] = type.Name;
                    continue;
                }
                var field = type.Field(selection.Name)!;
                engine._usage.AddOrUpdate($"{type.Name}.{field.Name}", 1, (_, n) => n + 1);

                var alive = Enumerable.Range(0, parents.Count).Where(i => results[i] is not null).ToList();
                if (alive.Count == 0)
                    break;
                var fieldPaths = alive.Select(i => (object[])[.. paths[i], selection.Key]).ToList();

                IReadOnlyList<object?> values;
                try
                {
                    values = field.Resolve(alive.Select(i => parents[i]).ToList(), arguments[selection]);
                    if (values.Count != alive.Count)
                        throw new InvalidOperationException($"{type.Name}.{field.Name}: {values.Count} values for {alive.Count} parents.");
                }
                catch (Exception e)   // a batched resolver that fails: the field is in error for all its parents
                {
                    var failure = new ResolutionFailure(e);
                    values = alive.Select(_ => (object?)failure).ToList();
                }

                var completed = Complete(field.Type, values, fieldPaths, selection);
                for (var k = 0; k < alive.Count; k++)
                {
                    if (completed[k].Invalid)
                        results[alive[k]] = null;
                    else
                        results[alive[k]]![selection.Key] = completed[k].Node;
                }
            }
            return results;
        }

        private List<Result> Complete(TypeRef type, IReadOnlyList<object?> values, IReadOnlyList<object[]> paths, Selection selection)
        {
            var inner = CompleteWithoutNullability(type, values, paths, selection);
            for (var k = 0; k < inner.Count; k++)
            {
                if (!type.NonNull)
                {
                    if (inner[k].Invalid)
                        inner[k] = new Result(false, null);   // a nullable field absorbs the error: it is null
                }
                else if (!inner[k].Invalid && inner[k].Node is null)
                {
                    errors.Add(Error($"Null value for the non-null field \"{selection.Name}\".", "INTERNAL_SERVER_ERROR", selection.Position, paths[k]));
                    inner[k] = new Result(true, null);
                }
            }
            return inner;
        }

        private List<Result> CompleteWithoutNullability(TypeRef type, IReadOnlyList<object?> values, IReadOnlyList<object[]> paths, Selection selection)
        {
            var results = new Result[values.Count];
            var present = new List<int>();
            for (var k = 0; k < values.Count; k++)
            {
                if (values[k] is ResolutionFailure failure)
                {
                    Report(failure.Exception, paths[k], selection.Position);
                    results[k] = new Result(true, null);
                }
                else if (values[k] is null)
                    results[k] = new Result(false, null);
                else
                    present.Add(k);
            }
            if (present.Count == 0)
                return [.. results];

            if (type.Element is not null)
            {
                // A list: the elements of all the lists of the level are flattened, completed together, regrouped.
                var elements = new List<object?>();
                var elementPaths = new List<object[]>();
                var owners = new List<int>();
                foreach (var k in present)
                {
                    var index = 0;
                    foreach (var element in (IEnumerable)values[k]!)
                    {
                        elements.Add(element);
                        elementPaths.Add([.. paths[k], index++]);
                        owners.Add(k);
                    }
                }
                var completed = Complete(type.Element, elements, elementPaths, selection);
                foreach (var k in present)
                {
                    var own = Enumerable.Range(0, elements.Count).Where(e => owners[e] == k).Select(e => completed[e]).ToList();
                    results[k] = own.Any(r => r.Invalid)
                        ? new Result(true, null)
                        : new Result(false, new JsonArray(own.Select(r => r.Node).ToArray()));
                }
                return [.. results];
            }

            switch (engine._schema.Type(type.Name!))
            {
                case ObjectType obj:
                    var children = ExecuteSelection(obj, present.Select(k => values[k]!).ToList(), present.Select(k => paths[k]).ToList(),
                        selection.SubSelection!);
                    for (var j = 0; j < present.Count; j++)
                        results[present[j]] = children[j] is null ? new Result(true, null) : new Result(false, children[j]);
                    break;
                case var leaf:
                    foreach (var k in present)
                    {
                        try
                        {
                            results[k] = new Result(false, Leaf(leaf!, values[k]!));
                        }
                        catch (Exception e)
                        {
                            Report(e, paths[k], selection.Position);
                            results[k] = new Result(true, null);
                        }
                    }
                    break;
            }
            return [.. results];
        }

        private static JsonNode Leaf(GraphQLType type, object value) => type switch
        {
            EnumType e => e.Values.Contains(value.ToString()!.ToUpperInvariant())
                ? JsonValue.Create(value.ToString()!.ToUpperInvariant())
                : throw new InvalidOperationException($"\"{value}\" is not a value of {e.Name}."),
            _ => type.Name switch
            {
                "ID" or "String" => JsonValue.Create(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))!,
                "Int" => JsonValue.Create(checked(Convert.ToInt32(value))),   // 32 bits: beyond, an error (no silent truncation)
                "Float" => JsonValue.Create(Convert.ToDouble(value)),           // a double: no decimal type in GraphQL
                "Boolean" => JsonValue.Create((bool)value),
                _ => throw new InvalidOperationException($"Unknown scalar {type.Name}.")
            }
        };

        private void Report(Exception exception, object[] path, Position position)
        {
            var error = exception as GraphQLError ?? engine._options.TranslateError?.Invoke(exception);
            errors.Add(error is null
                ? Error("Internal error.", "INTERNAL_SERVER_ERROR", position, path)
                : Error(error.Message, error.Code, position, path));
        }
    }

    // ─── Input values ───

    private sealed class Inputs(Schema schema)
    {
        public IReadOnlyDictionary<string, object?> Variables(IReadOnlyList<VariableDefinition> definitions, JsonObject? provided)
        {
            var values = new Dictionary<string, object?>();
            foreach (var definition in definitions)
            {
                var where = $"Variable \"${definition.Name}\"";
                if (provided is not null && provided.TryGetPropertyValue(definition.Name, out var node))
                    values[definition.Name] = Json(node, definition.Type, where);
                else if (definition.Default is not null)
                    values[definition.Name] = Literal(definition.Default, definition.Type, values, where);
                else if (definition.Type.NonNull)
                    throw new GraphQLError($"{where}: value required.", "BAD_USER_INPUT");
            }
            return values;
        }

        private object? Json(JsonNode? node, TypeRef type, string where)
        {
            if (node is null)
                return type.NonNull ? throw new GraphQLError($"{where}: value required.", "BAD_USER_INPUT") : null;
            if (node is not JsonValue value)
                throw Invalid(where, node.ToJsonString(), type);
            object? raw = value.GetValueKind() switch
            {
                System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number when value.TryGetValue<long>(out var l) => l,
                System.Text.Json.JsonValueKind.Number => value.GetValue<double>(),
                _ => throw Invalid(where, value.ToJsonString(), type)
            };
            // A JSON string is accepted for an enum (that is how the variables transmit them).
            if (raw is string s && schema.Type(type.BareName) is EnumType)
                return Literal(new EnumValue(s), type, new Dictionary<string, object?>(), where);
            return Literal(new LiteralValue(raw), type, new Dictionary<string, object?>(), where);
        }

        public object? Literal(Value value, TypeRef type, IReadOnlyDictionary<string, object?> variables, string where)
        {
            if (value is VariableValue variable)
            {
                var content = variables.GetValueOrDefault(variable.Name);
                return content is null && type.NonNull ? throw new GraphQLError($"{where}: value required.", "BAD_USER_INPUT") : content;
            }
            if (value is LiteralValue { Content: null })
                return type.NonNull ? throw new GraphQLError($"{where}: value required.", "BAD_USER_INPUT") : null;

            var target = schema.Type(type.BareName);
            return (target, value) switch
            {
                (EnumType e, EnumValue v) when e.Values.Contains(v.Name) => v.Name,
                (ScalarType { Name: "Int" }, LiteralValue { Content: long l }) when l is >= int.MinValue and <= int.MaxValue => (int)l,
                (ScalarType { Name: "Float" }, LiteralValue { Content: long l }) => (double)l,
                (ScalarType { Name: "Float" }, LiteralValue { Content: double d }) => d,
                (ScalarType { Name: "String" or "ID" }, LiteralValue { Content: string s }) => s,
                (ScalarType { Name: "ID" }, LiteralValue { Content: long l }) => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (ScalarType { Name: "Boolean" }, LiteralValue { Content: bool b }) => b,
                _ => throw Invalid(where, value switch { EnumValue e => e.Name, LiteralValue l => Convert.ToString(l.Content, System.Globalization.CultureInfo.InvariantCulture)!, _ => "?" }, type)
            };
        }

        private static GraphQLError Invalid(string where, string value, TypeRef type) =>
            new($"{where}: \"{value}\" is not a value of type {type}.", "BAD_USER_INPUT");
    }
}

using System.Globalization;
using System.Text;

namespace Styles.Tooling.GraphQL;

// ─── The tree of a query ───

public sealed record Position(int Line, int Column);

/// <summary>A type as written in a query or a schema: "Int", "ID!", "[Account!]!".</summary>
public sealed record TypeRef(string? Name, TypeRef? Element, bool NonNull)
{
    public static TypeRef Parse(string text) => new Parser(text).ReadSingleType();

    public TypeRef Nullable => this with { NonNull = false };

    public string BareName => Name ?? Element!.BareName;

    public override string ToString() => (Element is null ? Name : $"[{Element}]") + (NonNull ? "!" : "");
}

public abstract record Value;
public sealed record LiteralValue(object? Content) : Value;   // long, double, string, bool or null
public sealed record EnumValue(string Name) : Value;
public sealed record VariableValue(string Name) : Value;

public sealed record Argument(string Name, Value Value);

public sealed record Selection(string? Alias, string Name, IReadOnlyList<Argument> Arguments, IReadOnlyList<Selection>? SubSelection, Position Position)
{
    /// <summary>The name under which the field appears in the response.</summary>
    public string Key => Alias ?? Name;
}

public sealed record VariableDefinition(string Name, TypeRef Type, Value? Default, Position Position);

public sealed record OperationAst(string Type, string? Name, IReadOnlyList<VariableDefinition> Variables, IReadOnlyList<Selection> Selections);

public sealed class SyntaxError(string message, Position position) : Exception(message)
{
    public Position Position => position;
}

/// <summary>
/// The parsing of a GraphQL document: operations (query, mutation), variables, fields, aliases, arguments.
/// Not supported here: fragments, directives (@include, @skip), input types and lists as arguments, subscriptions.
/// </summary>
public sealed class Parser
{
    private enum Kind { Punctuation, Name, Integer, Float, String, End }

    private sealed record Token(Kind Kind, string Text, Position Position);

    private readonly List<Token> _tokens;
    private int _index;

    public Parser(string source) => _tokens = Tokenize(source);

    public static IReadOnlyList<OperationAst> ParseDocument(string source)
    {
        var parser = new Parser(source);
        var operations = new List<OperationAst>();
        while (parser.Current.Kind != Kind.End)
            operations.Add(parser.ReadOperation());
        if (operations.Count == 0)
            throw new SyntaxError("Empty document.", new Position(1, 1));
        return operations;
    }

    internal TypeRef ReadSingleType()
    {
        var type = ReadType();
        Expect(Kind.End);
        return type;
    }

    private Token Current => _tokens[_index];

    private Token Advance() => _tokens[_index++];

    private bool Is(string punctuation) => Current.Kind == Kind.Punctuation && Current.Text == punctuation;

    private bool IsName(string name) => Current.Kind == Kind.Name && Current.Text == name;

    private Token Expect(Kind kind, string? text = null)
    {
        if (Current.Kind != kind || (text is not null && Current.Text != text))
            throw new SyntaxError($"\"{text ?? kind.ToString()}\" expected, \"{(Current.Kind == Kind.End ? "end of document" : Current.Text)}\" found.", Current.Position);
        return Advance();
    }

    private OperationAst ReadOperation()
    {
        if (Is("{"))   // shorthand form: "{ customer(id: "alice") { name } }" is an anonymous query
            return new OperationAst("query", null, [], ReadSelections());

        var type = Expect(Kind.Name);
        if (type.Text == "fragment")
            throw new SyntaxError("Fragments are not supported by this engine.", type.Position);
        if (type.Text is not ("query" or "mutation"))
            throw new SyntaxError($"\"query\" or \"mutation\" expected, \"{type.Text}\" found.", type.Position);
        var name = Current.Kind == Kind.Name ? Advance().Text : null;

        var variables = new List<VariableDefinition>();
        if (Is("("))
        {
            Advance();
            while (!Is(")"))
            {
                var position = Expect(Kind.Punctuation, "$").Position;
                var variableName = Expect(Kind.Name).Text;
                Expect(Kind.Punctuation, ":");
                var variableType = ReadType();
                Value? @default = null;
                if (Is("="))
                {
                    Advance();
                    @default = ReadValue();
                }
                variables.Add(new VariableDefinition(variableName, variableType, @default, position));
            }
            Advance();
        }
        return new OperationAst(type.Text, name, variables, ReadSelections());
    }

    private List<Selection> ReadSelections()
    {
        Expect(Kind.Punctuation, "{");
        var selections = new List<Selection>();
        while (!Is("}"))
        {
            if (Is("..."))
                throw new SyntaxError("Fragments are not supported by this engine.", Current.Position);
            if (Is("@"))
                throw new SyntaxError("Directives are not supported by this engine.", Current.Position);
            var first = Expect(Kind.Name);
            string? alias = null;
            var name = first.Text;
            if (Is(":"))
            {
                Advance();
                alias = name;
                name = Expect(Kind.Name).Text;
            }
            var arguments = new List<Argument>();
            if (Is("("))
            {
                Advance();
                while (!Is(")"))
                {
                    var argumentName = Expect(Kind.Name).Text;
                    Expect(Kind.Punctuation, ":");
                    arguments.Add(new Argument(argumentName, ReadValue()));
                }
                Advance();
            }
            var subSelection = Is("{") ? ReadSelections() : null;
            selections.Add(new Selection(alias, name, arguments, subSelection, first.Position));
        }
        Advance();
        if (selections.Count == 0)
            throw new SyntaxError("A selection cannot be empty.", Current.Position);
        return selections;
    }

    private TypeRef ReadType()
    {
        TypeRef type;
        if (Is("["))
        {
            Advance();
            var element = ReadType();
            Expect(Kind.Punctuation, "]");
            type = new TypeRef(null, element, false);
        }
        else
            type = new TypeRef(Expect(Kind.Name).Text, null, false);
        if (Is("!"))
        {
            Advance();
            type = type with { NonNull = true };
        }
        return type;
    }

    private Value ReadValue()
    {
        var token = Advance();
        switch (token.Kind)
        {
            case Kind.Punctuation when token.Text == "$":
                return new VariableValue(Expect(Kind.Name).Text);
            case Kind.Integer:
                return new LiteralValue(long.Parse(token.Text, CultureInfo.InvariantCulture));
            case Kind.Float:
                return new LiteralValue(double.Parse(token.Text, CultureInfo.InvariantCulture));
            case Kind.String:
                return new LiteralValue(token.Text);
            case Kind.Name:
                return token.Text switch
                {
                    "true" => new LiteralValue(true),
                    "false" => new LiteralValue(false),
                    "null" => new LiteralValue(null),
                    _ => new EnumValue(token.Text)
                };
            default:
                throw new SyntaxError(token.Text is "[" or "{"
                    ? "Lists and objects as arguments are not supported by this engine."
                    : $"Value expected, \"{token.Text}\" found.", token.Position);
        }
    }

    // ─── Tokenizing ───

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        int i = 0, line = 1, lineStart = 0;
        Position Here() => new(line, i - lineStart + 1);

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\n')
            {
                i++;
                line++;
                lineStart = i;
            }
            else if (char.IsWhiteSpace(c) || c == ',' || c == '\uFEFF')   // commas do not count in GraphQL
                i++;
            else if (c == '#')
                while (i < source.Length && source[i] != '\n') i++;
            else if (source.AsSpan(i).StartsWith("..."))
            {
                tokens.Add(new Token(Kind.Punctuation, "...", Here()));
                i += 3;
            }
            else if ("!$():=@[]{}|".Contains(c))
            {
                tokens.Add(new Token(Kind.Punctuation, c.ToString(), Here()));
                i++;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                var position = Here();
                var start = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(Kind.Name, source[start..i], position));
            }
            else if (char.IsAsciiDigit(c) || c == '-')
            {
                var position = Here();
                var start = i++;
                var isFloat = false;
                while (i < source.Length && (char.IsAsciiDigit(source[i]) || source[i] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    isFloat |= source[i] is '.' or 'e' or 'E';
                    i++;
                }
                tokens.Add(new Token(isFloat ? Kind.Float : Kind.Integer, source[start..i], position));
            }
            else if (c == '"')
            {
                var position = Here();
                var text = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= source.Length || source[i] == '\n')
                        throw new SyntaxError("Unterminated string.", position);
                    var d = source[i++];
                    if (d == '"')
                        break;
                    if (d != '\\')
                    {
                        text.Append(d);
                        continue;
                    }
                    var escaped = i < source.Length ? source[i++] : ' ';
                    switch (escaped)
                    {
                        case 'n': text.Append('\n'); break;
                        case 't': text.Append('\t'); break;
                        case 'r': text.Append('\r'); break;
                        case '"' or '\\' or '/': text.Append(escaped); break;
                        case 'u' when i + 4 <= source.Length:
                            text.Append((char)Convert.ToInt32(source.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw new SyntaxError($"Invalid escape sequence \"\\{escaped}\".", position);
                    }
                }
                tokens.Add(new Token(Kind.String, text.ToString(), position));
            }
            else
                throw new SyntaxError($"Unexpected character \"{c}\".", Here());
        }
        tokens.Add(new Token(Kind.End, "", Here()));
        return tokens;
    }
}

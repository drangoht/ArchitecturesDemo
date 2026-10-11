using System.Globalization;
using System.Text;

namespace Styles.Outillage.GraphQL;

// ─── L'arbre d'une requête ───

public sealed record Position(int Ligne, int Colonne);

/// <summary>Un type tel qu'écrit dans une requête ou un schéma : « Int », « ID! », « [Compte!]! ».</summary>
public sealed record RefType(string? Nom, RefType? Element, bool NonNul)
{
    public static RefType Analyser(string texte) => new Analyseur(texte).LireTypeSeul();

    public RefType Nullable => this with { NonNul = false };

    public string NomNu => Nom ?? Element!.NomNu;

    public override string ToString() => (Element is null ? Nom : $"[{Element}]") + (NonNul ? "!" : "");
}

public abstract record Valeur;
public sealed record ValeurLitterale(object? Contenu) : Valeur;   // long, double, string, bool ou null
public sealed record ValeurEnum(string Nom) : Valeur;
public sealed record ValeurVariable(string Nom) : Valeur;

public sealed record Argument(string Nom, Valeur Valeur);

public sealed record Selection(string? Alias, string Nom, IReadOnlyList<Argument> Arguments, IReadOnlyList<Selection>? SousSelection, Position Position)
{
    /// <summary>Le nom sous lequel le champ apparaît dans la réponse.</summary>
    public string Cle => Alias ?? Nom;
}

public sealed record DefinitionVariable(string Nom, RefType Type, Valeur? ParDefaut, Position Position);

public sealed record OperationAst(string Type, string? Nom, IReadOnlyList<DefinitionVariable> Variables, IReadOnlyList<Selection> Selections);

public sealed class ErreurSyntaxe(string message, Position position) : Exception(message)
{
    public Position Position => position;
}

/// <summary>
/// L'analyse d'un document GraphQL : opérations (query, mutation), variables, champs, alias, arguments.
/// Non pris en charge ici : fragments, directives (@include, @skip), types d'entrée et listes en argument, abonnements.
/// </summary>
public sealed class Analyseur
{
    private enum Genre { Ponctuation, Nom, Entier, Flottant, Chaine, Fin }

    private sealed record Jeton(Genre Genre, string Texte, Position Position);

    private readonly List<Jeton> _jetons;
    private int _index;

    public Analyseur(string source) => _jetons = Decouper(source);

    public static IReadOnlyList<OperationAst> AnalyserDocument(string source)
    {
        var analyseur = new Analyseur(source);
        var operations = new List<OperationAst>();
        while (analyseur.Courant.Genre != Genre.Fin)
            operations.Add(analyseur.LireOperation());
        if (operations.Count == 0)
            throw new ErreurSyntaxe("Document vide.", new Position(1, 1));
        return operations;
    }

    internal RefType LireTypeSeul()
    {
        var type = LireType();
        Attendre(Genre.Fin);
        return type;
    }

    private Jeton Courant => _jetons[_index];

    private Jeton Avancer() => _jetons[_index++];

    private bool Est(string ponctuation) => Courant.Genre == Genre.Ponctuation && Courant.Texte == ponctuation;

    private bool EstNom(string nom) => Courant.Genre == Genre.Nom && Courant.Texte == nom;

    private Jeton Attendre(Genre genre, string? texte = null)
    {
        if (Courant.Genre != genre || (texte is not null && Courant.Texte != texte))
            throw new ErreurSyntaxe($"« {texte ?? genre.ToString()} » attendu, « {(Courant.Genre == Genre.Fin ? "fin du document" : Courant.Texte)} » trouvé.", Courant.Position);
        return Avancer();
    }

    private OperationAst LireOperation()
    {
        if (Est("{"))   // forme abrégée : « { client(id: "alice") { nom } } » est une query anonyme
            return new OperationAst("query", null, [], LireSelections());

        var type = Attendre(Genre.Nom);
        if (type.Texte == "fragment")
            throw new ErreurSyntaxe("Les fragments ne sont pas pris en charge par ce moteur.", type.Position);
        if (type.Texte is not ("query" or "mutation"))
            throw new ErreurSyntaxe($"« query » ou « mutation » attendu, « {type.Texte} » trouvé.", type.Position);
        var nom = Courant.Genre == Genre.Nom ? Avancer().Texte : null;

        var variables = new List<DefinitionVariable>();
        if (Est("("))
        {
            Avancer();
            while (!Est(")"))
            {
                var position = Attendre(Genre.Ponctuation, "$").Position;
                var nomVariable = Attendre(Genre.Nom).Texte;
                Attendre(Genre.Ponctuation, ":");
                var typeVariable = LireType();
                Valeur? parDefaut = null;
                if (Est("="))
                {
                    Avancer();
                    parDefaut = LireValeur();
                }
                variables.Add(new DefinitionVariable(nomVariable, typeVariable, parDefaut, position));
            }
            Avancer();
        }
        return new OperationAst(type.Texte, nom, variables, LireSelections());
    }

    private List<Selection> LireSelections()
    {
        Attendre(Genre.Ponctuation, "{");
        var selections = new List<Selection>();
        while (!Est("}"))
        {
            if (Est("..."))
                throw new ErreurSyntaxe("Les fragments ne sont pas pris en charge par ce moteur.", Courant.Position);
            if (Est("@"))
                throw new ErreurSyntaxe("Les directives ne sont pas prises en charge par ce moteur.", Courant.Position);
            var premier = Attendre(Genre.Nom);
            string? alias = null;
            var nom = premier.Texte;
            if (Est(":"))
            {
                Avancer();
                alias = nom;
                nom = Attendre(Genre.Nom).Texte;
            }
            var arguments = new List<Argument>();
            if (Est("("))
            {
                Avancer();
                while (!Est(")"))
                {
                    var nomArgument = Attendre(Genre.Nom).Texte;
                    Attendre(Genre.Ponctuation, ":");
                    arguments.Add(new Argument(nomArgument, LireValeur()));
                }
                Avancer();
            }
            var sousSelection = Est("{") ? LireSelections() : null;
            selections.Add(new Selection(alias, nom, arguments, sousSelection, premier.Position));
        }
        Avancer();
        if (selections.Count == 0)
            throw new ErreurSyntaxe("Une sélection ne peut pas être vide.", Courant.Position);
        return selections;
    }

    private RefType LireType()
    {
        RefType type;
        if (Est("["))
        {
            Avancer();
            var element = LireType();
            Attendre(Genre.Ponctuation, "]");
            type = new RefType(null, element, false);
        }
        else
            type = new RefType(Attendre(Genre.Nom).Texte, null, false);
        if (Est("!"))
        {
            Avancer();
            type = type with { NonNul = true };
        }
        return type;
    }

    private Valeur LireValeur()
    {
        var jeton = Avancer();
        switch (jeton.Genre)
        {
            case Genre.Ponctuation when jeton.Texte == "$":
                return new ValeurVariable(Attendre(Genre.Nom).Texte);
            case Genre.Entier:
                return new ValeurLitterale(long.Parse(jeton.Texte, CultureInfo.InvariantCulture));
            case Genre.Flottant:
                return new ValeurLitterale(double.Parse(jeton.Texte, CultureInfo.InvariantCulture));
            case Genre.Chaine:
                return new ValeurLitterale(jeton.Texte);
            case Genre.Nom:
                return jeton.Texte switch
                {
                    "true" => new ValeurLitterale(true),
                    "false" => new ValeurLitterale(false),
                    "null" => new ValeurLitterale(null),
                    _ => new ValeurEnum(jeton.Texte)
                };
            default:
                throw new ErreurSyntaxe(jeton.Texte is "[" or "{"
                    ? "Les listes et objets en argument ne sont pas pris en charge par ce moteur."
                    : $"Valeur attendue, « {jeton.Texte} » trouvé.", jeton.Position);
        }
    }

    // ─── Découpage en jetons ───

    private static List<Jeton> Decouper(string source)
    {
        var jetons = new List<Jeton>();
        int i = 0, ligne = 1, debutLigne = 0;
        Position Ici() => new(ligne, i - debutLigne + 1);

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\n')
            {
                i++;
                ligne++;
                debutLigne = i;
            }
            else if (char.IsWhiteSpace(c) || c == ',' || c == '﻿')   // les virgules ne comptent pas en GraphQL
                i++;
            else if (c == '#')
                while (i < source.Length && source[i] != '\n') i++;
            else if (source.AsSpan(i).StartsWith("..."))
            {
                jetons.Add(new Jeton(Genre.Ponctuation, "...", Ici()));
                i += 3;
            }
            else if ("!$():=@[]{}|".Contains(c))
            {
                jetons.Add(new Jeton(Genre.Ponctuation, c.ToString(), Ici()));
                i++;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                var position = Ici();
                var debut = i;
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;
                jetons.Add(new Jeton(Genre.Nom, source[debut..i], position));
            }
            else if (char.IsAsciiDigit(c) || c == '-')
            {
                var position = Ici();
                var debut = i++;
                var flottant = false;
                while (i < source.Length && (char.IsAsciiDigit(source[i]) || source[i] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    flottant |= source[i] is '.' or 'e' or 'E';
                    i++;
                }
                jetons.Add(new Jeton(flottant ? Genre.Flottant : Genre.Entier, source[debut..i], position));
            }
            else if (c == '"')
            {
                var position = Ici();
                var texte = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= source.Length || source[i] == '\n')
                        throw new ErreurSyntaxe("Chaîne non terminée.", position);
                    var d = source[i++];
                    if (d == '"')
                        break;
                    if (d != '\\')
                    {
                        texte.Append(d);
                        continue;
                    }
                    var echappe = i < source.Length ? source[i++] : ' ';
                    switch (echappe)
                    {
                        case 'n': texte.Append('\n'); break;
                        case 't': texte.Append('\t'); break;
                        case 'r': texte.Append('\r'); break;
                        case '"' or '\\' or '/': texte.Append(echappe); break;
                        case 'u' when i + 4 <= source.Length:
                            texte.Append((char)Convert.ToInt32(source.Substring(i, 4), 16));
                            i += 4;
                            break;
                        default: throw new ErreurSyntaxe($"Séquence d'échappement « \\{echappe} » invalide.", position);
                    }
                }
                jetons.Add(new Jeton(Genre.Chaine, texte.ToString(), position));
            }
            else
                throw new ErreurSyntaxe($"Caractère inattendu « {c} ».", Ici());
        }
        jetons.Add(new Jeton(Genre.Fin, "", Ici()));
        return jetons;
    }
}

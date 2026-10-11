using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Styles.Outillage.GraphQL;

/// <summary>Une erreur à montrer telle quelle au client, avec son code (dans « extensions.code »).</summary>
public sealed class ErreurGraphQL(string message, string code) : Exception(message)
{
    public string Code => code;
}

public sealed class OptionsGraphQL
{
    /// <summary>
    /// Le nombre maximal de niveaux de sélection imbriqués (« { client { nom } } » en a deux). Le client choisit sa requête : sans limite, « client → comptes → titulaire →
    /// comptes → … » ferait travailler le serveur sans fin (en production : aussi un coût maximal par requête).
    /// </summary>
    public int ProfondeurMax { get; init; } = 6;

    /// <summary>Traduit une exception d'un résolveur en erreur montrable ; sinon, « Erreur interne. » sans détail.</summary>
    public Func<Exception, ErreurGraphQL?>? TraduireErreur { get; init; }
}

/// <summary>Ce que rend un résolveur qui a échoué pour un parent : le champ vaudra null et une erreur sera ajoutée.</summary>
internal sealed class EchecResolution(Exception exception)
{
    public Exception Exception => exception;
}

/// <summary>
/// Le moteur : analyse, validation contre le schéma, conversion des variables et arguments, puis exécution.
/// L'exécution se fait par NIVEAUX : un champ est résolu pour tous les objets d'un même niveau à la fois, ce qui permet
/// aux résolveurs groupés de faire une seule lecture là où il en faudrait une par objet (le problème « N+1 »).
/// </summary>
public sealed class MoteurGraphQL
{
    private readonly Schema _schema;
    private readonly OptionsGraphQL _options;
    private readonly ConcurrentDictionary<string, long> _utilisation = new();

    public MoteurGraphQL(Schema schema, OptionsGraphQL? options = null)
    {
        schema.Verifier();
        _schema = schema;
        _options = options ?? new OptionsGraphQL();
    }

    public Schema Schema => _schema;

    /// <summary>
    /// Combien de requêtes ont demandé chaque champ. Le serveur GraphQL sait exactement ce que ses clients lisent :
    /// on sait quand plus personne n'utilise un champ déprécié, et on peut alors le retirer.
    /// </summary>
    public IReadOnlyDictionary<string, long> Utilisation =>
        new SortedDictionary<string, long>(_utilisation.ToDictionary(), StringComparer.Ordinal);

    public JsonObject Executer(string? requete, JsonObject? variables = null, string? nomOperation = null)
    {
        var erreurs = new JsonArray();

        IReadOnlyList<OperationAst> operations;
        try
        {
            operations = Analyseur.AnalyserDocument(requete ?? "");
        }
        catch (ErreurSyntaxe e)
        {
            return new JsonObject { ["errors"] = new JsonArray(Erreur(e.Message, "GRAPHQL_PARSE_FAILED", e.Position)) };
        }

        var operation = nomOperation is null
            ? operations.Count == 1 ? operations[0] : null
            : operations.FirstOrDefault(o => o.Nom == nomOperation);
        var racine = operation?.Type == "mutation" ? _schema.Mutation : _schema.Query;
        if (operation is null || racine is null)
        {
            var message = operation is null
                ? nomOperation is null ? "Plusieurs opérations dans le document : précisez « operationName »." : $"Opération « {nomOperation} » introuvable."
                : "Ce schéma n'a pas de mutations.";
            return new JsonObject { ["errors"] = new JsonArray(Erreur(message, "GRAPHQL_VALIDATION_FAILED")) };
        }

        // 1. Valider la requête contre le schéma, AVANT d'exécuter quoi que ce soit.
        var validation = new Validation(_schema, _options, operation);
        validation.Valider(racine, operation.Selections, profondeur: 1);
        validation.VerifierVariables();
        if (validation.Erreurs.Count > 0)
            return new JsonObject { ["errors"] = new JsonArray(validation.Erreurs.ToArray<JsonNode?>()) };

        // 2. Convertir variables et arguments selon les types déclarés.
        Dictionary<Selection, Arguments> arguments;
        try
        {
            var valeursVariables = new Entrees(_schema).Variables(operation.Variables, variables);
            arguments = new Dictionary<Selection, Arguments>(ReferenceEqualityComparer.Instance);
            CalculerArguments(racine, operation.Selections, valeursVariables, arguments);
        }
        catch (ErreurGraphQL e)
        {
            return new JsonObject { ["errors"] = new JsonArray(Erreur(e.Message, e.Code)) };
        }

        // 3. Exécuter. Les erreurs des résolveurs n'arrêtent pas tout : les autres champs sont servis (réponse partielle).
        var execution = new Execution(this, arguments, erreurs);
        var donnees = execution.ExecuterSelection(racine, [Racine.Instance], [[]], operation.Selections)[0];

        var reponse = new JsonObject { ["data"] = donnees };
        if (erreurs.Count > 0)
            reponse["errors"] = erreurs;
        return reponse;
    }

    private void CalculerArguments(TypeObjet type, IReadOnlyList<Selection> selections, IReadOnlyDictionary<string, object?> variables,
        Dictionary<Selection, Arguments> resultat)
    {
        var entrees = new Entrees(_schema);
        foreach (var selection in selections)
        {
            if (type.Champ(selection.Nom) is not { } champ)
                continue;   // __typename
            var valeurs = new Dictionary<string, object?>();
            foreach (var arg in champ.Args)
            {
                var fourni = selection.Arguments.FirstOrDefault(a => a.Nom == arg.Nom);
                if (fourni is null)
                {
                    if (arg.ParDefaut is not null)
                        valeurs[arg.Nom] = arg.ParDefaut;
                    continue;
                }
                if (fourni.Valeur is ValeurVariable v && !variables.ContainsKey(v.Nom))
                {
                    if (arg.ParDefaut is not null)
                        valeurs[arg.Nom] = arg.ParDefaut;   // variable facultative non fournie : l'argument garde son défaut
                    continue;
                }
                valeurs[arg.Nom] = entrees.Litteral(fourni.Valeur, arg.RefType, variables, $"Argument « {arg.Nom} » de « {type.Nom}.{champ.Nom} »");
            }
            resultat[selection] = new Arguments(valeurs);
            if (selection.SousSelection is not null && _schema.Type(champ.Type.NomNu) is TypeObjet sousType)
                CalculerArguments(sousType, selection.SousSelection, variables, resultat);
        }
    }

    internal static JsonObject Erreur(string message, string code, Position? position = null, IEnumerable<object>? chemin = null)
    {
        var erreur = new JsonObject { ["message"] = message };
        if (position is not null)
            erreur["locations"] = new JsonArray(new JsonObject { ["line"] = position.Ligne, ["column"] = position.Colonne });
        if (chemin is not null)
            erreur["path"] = new JsonArray(chemin.Select(e => e is int i ? JsonValue.Create(i) : (JsonNode)JsonValue.Create((string)e)).ToArray<JsonNode?>());
        erreur["extensions"] = new JsonObject { ["code"] = code };
        return erreur;
    }

    // ─── Validation ───

    private sealed class Validation(Schema schema, OptionsGraphQL options, OperationAst operation)
    {
        private readonly HashSet<string> _variablesUtilisees = [];
        private bool _tropProfond;

        public List<JsonObject> Erreurs { get; } = [];

        private void Signaler(string message, Position position) =>
            Erreurs.Add(Erreur(message, "GRAPHQL_VALIDATION_FAILED", position));

        public void Valider(TypeObjet type, IReadOnlyList<Selection> selections, int profondeur)
        {
            if (profondeur > options.ProfondeurMax && !_tropProfond)
            {
                _tropProfond = true;
                Signaler($"La requête dépasse la profondeur maximale de {options.ProfondeurMax} niveaux.", selections[0].Position);
            }
            foreach (var doublon in selections.GroupBy(s => s.Cle).Where(g => g.Count() > 1))
                Signaler($"« {doublon.Key} » apparaît deux fois au même niveau : utilisez des alias.", doublon.Last().Position);

            foreach (var selection in selections)
            {
                if (selection.Nom == "__typename")
                {
                    if (selection.SousSelection is not null)
                        Signaler("« __typename » n'a pas de sous-champs.", selection.Position);
                    continue;
                }
                if (type.Champ(selection.Nom) is not { } champ)
                {
                    Signaler($"Le champ « {selection.Nom} » n'existe pas sur le type « {type.Nom} ».", selection.Position);
                    continue;
                }
                foreach (var argument in selection.Arguments)
                {
                    if (champ.Args.FirstOrDefault(a => a.Nom == argument.Nom) is not { } declare)
                        Signaler($"L'argument « {argument.Nom} » n'existe pas sur « {type.Nom}.{champ.Nom} ».", selection.Position);
                    else if (argument.Valeur is ValeurVariable v)
                        VerifierUsageVariable(v.Nom, declare, selection.Position);
                }
                foreach (var manquant in champ.Args.Where(a => a.RefType.NonNul && a.ParDefaut is null && selection.Arguments.All(s => s.Nom != a.Nom)))
                    Signaler($"L'argument « {manquant.Nom} » est obligatoire pour « {type.Nom}.{champ.Nom} ».", selection.Position);

                if (schema.Type(champ.Type.NomNu) is TypeObjet sousType)
                {
                    if (selection.SousSelection is null)
                        Signaler($"« {type.Nom}.{champ.Nom} » est de type {champ.Type} : précisez les sous-champs voulus.", selection.Position);
                    else
                        Valider(sousType, selection.SousSelection, profondeur + 1);
                }
                else if (selection.SousSelection is not null)
                    Signaler($"« {type.Nom}.{champ.Nom} » est de type {champ.Type} : il n'a pas de sous-champs.", selection.Position);
            }
        }

        private void VerifierUsageVariable(string nom, Arg argument, Position position)
        {
            _variablesUtilisees.Add(nom);
            if (operation.Variables.FirstOrDefault(d => d.Nom == nom) is not { } definition)
                Signaler($"La variable « ${nom} » n'est pas déclarée.", position);
            else if (definition.Type.NomNu != argument.RefType.NomNu
                     || (argument.RefType.NonNul && !definition.Type.NonNul && definition.ParDefaut is null))
                Signaler($"La variable « ${nom} » ({definition.Type}) ne convient pas à l'argument « {argument.Nom} » ({argument.Type}).", position);
        }

        public void VerifierVariables()
        {
            foreach (var definition in operation.Variables)
            {
                if (!_variablesUtilisees.Contains(definition.Nom))
                    Signaler($"La variable « ${definition.Nom} » est déclarée mais pas utilisée.", definition.Position);
                if (schema.Type(definition.Type.NomNu) is null or TypeObjet || definition.Type.Element is not null)
                    Signaler($"La variable « ${definition.Nom} » : type {definition.Type} non accepté en entrée.", definition.Position);
            }
        }
    }

    // ─── Exécution ───

    private sealed class Execution(MoteurGraphQL moteur, Dictionary<Selection, Arguments> arguments, JsonArray erreurs)
    {
        private readonly record struct Resultat(bool Invalide, JsonNode? Noeud);

        /// <summary>
        /// Résout une sélection pour tous les parents d'un niveau. Rend, pour chaque parent, son objet JSON, ou null si
        /// un champ non nul n'a pas pu être obtenu (l'objet entier devient alors null : propagation des null).
        /// </summary>
        public List<JsonObject?> ExecuterSelection(TypeObjet type, IReadOnlyList<object> parents, IReadOnlyList<object[]> chemins,
            IReadOnlyList<Selection> selections)
        {
            var resultats = parents.Select(_ => (JsonObject?)new JsonObject()).ToList();
            foreach (var selection in selections)
            {
                if (selection.Nom == "__typename")
                {
                    foreach (var objet in resultats.OfType<JsonObject>())
                        objet[selection.Cle] = type.Nom;
                    continue;
                }
                var champ = type.Champ(selection.Nom)!;
                moteur._utilisation.AddOrUpdate($"{type.Nom}.{champ.Nom}", 1, (_, n) => n + 1);

                var vivants = Enumerable.Range(0, parents.Count).Where(i => resultats[i] is not null).ToList();
                if (vivants.Count == 0)
                    break;
                var cheminsChamp = vivants.Select(i => (object[])[.. chemins[i], selection.Cle]).ToList();

                IReadOnlyList<object?> valeurs;
                try
                {
                    valeurs = champ.Resoudre(vivants.Select(i => parents[i]).ToList(), arguments[selection]);
                    if (valeurs.Count != vivants.Count)
                        throw new InvalidOperationException($"{type.Nom}.{champ.Nom} : {valeurs.Count} valeurs pour {vivants.Count} parents.");
                }
                catch (Exception e)   // un résolveur groupé qui échoue : le champ est en erreur pour tous ses parents
                {
                    var echec = new EchecResolution(e);
                    valeurs = vivants.Select(_ => (object?)echec).ToList();
                }

                var completes = Completer(champ.Type, valeurs, cheminsChamp, selection);
                for (var k = 0; k < vivants.Count; k++)
                {
                    if (completes[k].Invalide)
                        resultats[vivants[k]] = null;
                    else
                        resultats[vivants[k]]![selection.Cle] = completes[k].Noeud;
                }
            }
            return resultats;
        }

        private List<Resultat> Completer(RefType type, IReadOnlyList<object?> valeurs, IReadOnlyList<object[]> chemins, Selection selection)
        {
            var interne = CompleterSansNullabilite(type, valeurs, chemins, selection);
            for (var k = 0; k < interne.Count; k++)
            {
                if (!type.NonNul)
                {
                    if (interne[k].Invalide)
                        interne[k] = new Resultat(false, null);   // un champ nullable absorbe l'erreur : il vaut null
                }
                else if (!interne[k].Invalide && interne[k].Noeud is null)
                {
                    erreurs.Add(Erreur($"Valeur nulle pour le champ non nul « {selection.Nom} ».", "INTERNAL_SERVER_ERROR", selection.Position, chemins[k]));
                    interne[k] = new Resultat(true, null);
                }
            }
            return interne;
        }

        private List<Resultat> CompleterSansNullabilite(RefType type, IReadOnlyList<object?> valeurs, IReadOnlyList<object[]> chemins, Selection selection)
        {
            var resultats = new Resultat[valeurs.Count];
            var presents = new List<int>();
            for (var k = 0; k < valeurs.Count; k++)
            {
                if (valeurs[k] is EchecResolution echec)
                {
                    Signaler(echec.Exception, chemins[k], selection.Position);
                    resultats[k] = new Resultat(true, null);
                }
                else if (valeurs[k] is null)
                    resultats[k] = new Resultat(false, null);
                else
                    presents.Add(k);
            }
            if (presents.Count == 0)
                return [.. resultats];

            if (type.Element is not null)
            {
                // Une liste : on aplatit les éléments de toutes les listes du niveau, on les complète ensemble, on regroupe.
                var elements = new List<object?>();
                var cheminsElements = new List<object[]>();
                var proprietaires = new List<int>();
                foreach (var k in presents)
                {
                    var index = 0;
                    foreach (var element in (IEnumerable)valeurs[k]!)
                    {
                        elements.Add(element);
                        cheminsElements.Add([.. chemins[k], index++]);
                        proprietaires.Add(k);
                    }
                }
                var completes = Completer(type.Element, elements, cheminsElements, selection);
                foreach (var k in presents)
                {
                    var siens = Enumerable.Range(0, elements.Count).Where(e => proprietaires[e] == k).Select(e => completes[e]).ToList();
                    resultats[k] = siens.Any(r => r.Invalide)
                        ? new Resultat(true, null)
                        : new Resultat(false, new JsonArray(siens.Select(r => r.Noeud).ToArray()));
                }
                return [.. resultats];
            }

            switch (moteur._schema.Type(type.Nom!))
            {
                case TypeObjet objet:
                    var enfants = ExecuterSelection(objet, presents.Select(k => valeurs[k]!).ToList(), presents.Select(k => chemins[k]).ToList(),
                        selection.SousSelection!);
                    for (var j = 0; j < presents.Count; j++)
                        resultats[presents[j]] = enfants[j] is null ? new Resultat(true, null) : new Resultat(false, enfants[j]);
                    break;
                case var feuille:
                    foreach (var k in presents)
                    {
                        try
                        {
                            resultats[k] = new Resultat(false, Feuille(feuille!, valeurs[k]!));
                        }
                        catch (Exception e)
                        {
                            Signaler(e, chemins[k], selection.Position);
                            resultats[k] = new Resultat(true, null);
                        }
                    }
                    break;
            }
            return [.. resultats];
        }

        private static JsonNode Feuille(TypeGraphQL type, object valeur) => type switch
        {
            TypeEnum e => e.Valeurs.Contains(valeur.ToString()!.ToUpperInvariant())
                ? JsonValue.Create(valeur.ToString()!.ToUpperInvariant())
                : throw new InvalidOperationException($"« {valeur} » n'est pas une valeur de {e.Nom}."),
            _ => type.Nom switch
            {
                "ID" or "String" => JsonValue.Create(Convert.ToString(valeur, System.Globalization.CultureInfo.InvariantCulture))!,
                "Int" => JsonValue.Create(checked(Convert.ToInt32(valeur))),   // 32 bits : au-delà, erreur (pas de troncature silencieuse)
                "Float" => JsonValue.Create(Convert.ToDouble(valeur)),           // un double : pas de type décimal en GraphQL
                "Boolean" => JsonValue.Create((bool)valeur),
                _ => throw new InvalidOperationException($"Scalaire {type.Nom} inconnu.")
            }
        };

        private void Signaler(Exception exception, object[] chemin, Position position)
        {
            var erreur = exception as ErreurGraphQL ?? moteur._options.TraduireErreur?.Invoke(exception);
            erreurs.Add(erreur is null
                ? Erreur("Erreur interne.", "INTERNAL_SERVER_ERROR", position, chemin)
                : Erreur(erreur.Message, erreur.Code, position, chemin));
        }
    }

    // ─── Valeurs d'entrée ───

    private sealed class Entrees(Schema schema)
    {
        public IReadOnlyDictionary<string, object?> Variables(IReadOnlyList<DefinitionVariable> definitions, JsonObject? fournies)
        {
            var valeurs = new Dictionary<string, object?>();
            foreach (var definition in definitions)
            {
                var ou = $"Variable « ${definition.Nom} »";
                if (fournies is not null && fournies.TryGetPropertyValue(definition.Nom, out var noeud))
                    valeurs[definition.Nom] = Json(noeud, definition.Type, ou);
                else if (definition.ParDefaut is not null)
                    valeurs[definition.Nom] = Litteral(definition.ParDefaut, definition.Type, valeurs, ou);
                else if (definition.Type.NonNul)
                    throw new ErreurGraphQL($"{ou} : valeur obligatoire.", "BAD_USER_INPUT");
            }
            return valeurs;
        }

        private object? Json(JsonNode? noeud, RefType type, string ou)
        {
            if (noeud is null)
                return type.NonNul ? throw new ErreurGraphQL($"{ou} : valeur obligatoire.", "BAD_USER_INPUT") : null;
            if (noeud is not JsonValue valeur)
                throw Invalide(ou, noeud.ToJsonString(), type);
            object? brut = valeur.GetValueKind() switch
            {
                System.Text.Json.JsonValueKind.String => valeur.GetValue<string>(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number when valeur.TryGetValue<long>(out var l) => l,
                System.Text.Json.JsonValueKind.Number => valeur.GetValue<double>(),
                _ => throw Invalide(ou, valeur.ToJsonString(), type)
            };
            // Une chaîne JSON est acceptée pour un enum (c'est ainsi que les variables les transmettent).
            if (brut is string s && schema.Type(type.NomNu) is TypeEnum)
                return Litteral(new ValeurEnum(s), type, new Dictionary<string, object?>(), ou);
            return Litteral(new ValeurLitterale(brut), type, new Dictionary<string, object?>(), ou);
        }

        public object? Litteral(Valeur valeur, RefType type, IReadOnlyDictionary<string, object?> variables, string ou)
        {
            if (valeur is ValeurVariable variable)
            {
                var contenu = variables.GetValueOrDefault(variable.Nom);
                return contenu is null && type.NonNul ? throw new ErreurGraphQL($"{ou} : valeur obligatoire.", "BAD_USER_INPUT") : contenu;
            }
            if (valeur is ValeurLitterale { Contenu: null })
                return type.NonNul ? throw new ErreurGraphQL($"{ou} : valeur obligatoire.", "BAD_USER_INPUT") : null;

            var cible = schema.Type(type.NomNu);
            return (cible, valeur) switch
            {
                (TypeEnum e, ValeurEnum v) when e.Valeurs.Contains(v.Nom) => v.Nom,
                (TypeScalaire { Nom: "Int" }, ValeurLitterale { Contenu: long l }) when l is >= int.MinValue and <= int.MaxValue => (int)l,
                (TypeScalaire { Nom: "Float" }, ValeurLitterale { Contenu: long l }) => (double)l,
                (TypeScalaire { Nom: "Float" }, ValeurLitterale { Contenu: double d }) => d,
                (TypeScalaire { Nom: "String" or "ID" }, ValeurLitterale { Contenu: string s }) => s,
                (TypeScalaire { Nom: "ID" }, ValeurLitterale { Contenu: long l }) => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (TypeScalaire { Nom: "Boolean" }, ValeurLitterale { Contenu: bool b }) => b,
                _ => throw Invalide(ou, valeur switch { ValeurEnum e => e.Nom, ValeurLitterale l => Convert.ToString(l.Contenu, System.Globalization.CultureInfo.InvariantCulture)!, _ => "?" }, type)
            };
        }

        private static ErreurGraphQL Invalide(string ou, string valeur, RefType type) =>
            new($"{ou} : « {valeur} » n'est pas une valeur de type {type}.", "BAD_USER_INPUT");
    }
}

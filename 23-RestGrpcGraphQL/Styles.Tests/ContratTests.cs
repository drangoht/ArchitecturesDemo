using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Styles.Contrats;
using Styles.Outillage.Protobuf;

namespace Styles.Tests;

/// <summary>
/// banque.proto est la source de vérité du contrat gRPC. Sans protoc pour générer les classes, ce test vérifie qu'elles
/// lui correspondent exactement : même champs, mêmes numéros, mêmes types ; et que le serveur sert chaque méthode.
/// </summary>
public class ContratTests
{
    private static readonly string Proto = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "banque.proto"));

    private static string Pascal(string snake) =>
        string.Concat(snake.ToLowerInvariant().Split('_').Select(m => char.ToUpperInvariant(m[0]) + m[1..]));

    private static string TypeProto(PropertyInfo propriete, ChampAttribute champ)
    {
        var type = propriete.PropertyType;
        var repete = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        if (repete)
            type = type.GetGenericArguments()[0];
        var nom = type == typeof(string) ? "string"
            : type == typeof(int) ? "int32"
            : type == typeof(long) ? champ.ZigZag ? "sint64" : "int64"
            : type == typeof(bool) ? "bool"
            : type.Name;
        return (repete ? "repeated " : "") + nom;
    }

    [Fact]
    public void Chaque_message_du_proto_correspond_a_sa_classe_champ_par_champ()
    {
        var messages = Regex.Matches(Proto, @"message (\w+) \{([^}]*)\}");
        Assert.Equal(8, messages.Count);
        var classes = typeof(Compte).Assembly.GetTypes()
            .Where(t => t.GetProperties().Any(p => p.GetCustomAttribute<ChampAttribute>() is not null))
            .ToDictionary(t => t.Name);

        foreach (Match message in messages)
        {
            var nom = message.Groups[1].Value;
            Assert.True(classes.Remove(nom, out var classe), $"Pas de classe pour le message {nom}.");
            var attendus = Regex.Matches(message.Groups[2].Value, @"^\s*((?:repeated )?\w+) (\w+) = (\d+);", RegexOptions.Multiline)
                .Select(m => $"{Pascal(m.Groups[2].Value)} = {m.Groups[3].Value} : {m.Groups[1].Value}")
                .Order();
            var trouves = classe!.GetProperties()
                .Select(p => (Propriete: p, Champ: p.GetCustomAttribute<ChampAttribute>()))
                .Where(x => x.Champ is not null)
                .Select(x => $"{x.Propriete.Name} = {x.Champ!.Numero} : {TypeProto(x.Propriete, x.Champ)}")
                .Order();
            Assert.Equal(attendus, trouves);
        }
        Assert.Empty(classes.Keys);   // aucune classe sans message dans le contrat
    }

    [Fact]
    public void L_enumeration_du_proto_correspond_a_celle_du_code()
    {
        var valeurs = Regex.Match(Proto, @"enum TypeCompte \{([^}]*)\}").Groups[1].Value;
        var attendues = Regex.Matches(valeurs, @"(\w+) = (\d+);").Select(m => (Pascal(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
        var trouvees = Enum.GetValues<TypeCompte>().Select(v => (v.ToString(), (int)v));
        Assert.Equal(attendues, trouvees);
    }

    [Fact]
    public async Task Le_serveur_sert_chaque_methode_du_service_et_rien_d_autre()
    {
        await using var systeme = new Systeme();
        var attendues = Regex.Matches(Proto, @"rpc (\w+) \(").Select(m => $"/{ServiceBanque.Nom}/{m.Groups[1].Value}").Order();
        var routes = systeme.Serveur.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Where(r => r.StartsWith($"/{ServiceBanque.Nom}/"))
            .Order();
        Assert.Equal(attendues, routes);
    }
}

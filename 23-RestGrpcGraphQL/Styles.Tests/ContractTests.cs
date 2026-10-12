using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Styles.Contracts;
using Styles.Tooling.Protobuf;

namespace Styles.Tests;

/// <summary>
/// bank.proto is the source of truth of the gRPC contract. Without protoc to generate the classes, this test checks that they
/// match it exactly: same fields, same numbers, same types; and that the server serves each method.
/// </summary>
public class ContractTests
{
    private static readonly string Proto = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bank.proto"));

    private static string Pascal(string snake) =>
        string.Concat(snake.ToLowerInvariant().Split('_').Select(m => char.ToUpperInvariant(m[0]) + m[1..]));

    private static string ProtoType(PropertyInfo property, FieldAttribute field)
    {
        var type = property.PropertyType;
        var repeated = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        if (repeated)
            type = type.GetGenericArguments()[0];
        var name = type == typeof(string) ? "string"
            : type == typeof(int) ? "int32"
            : type == typeof(long) ? field.ZigZag ? "sint64" : "int64"
            : type == typeof(bool) ? "bool"
            : type.Name;
        return (repeated ? "repeated " : "") + name;
    }

    [Fact]
    public void Each_message_of_the_proto_matches_its_class_field_by_field()
    {
        var messages = Regex.Matches(Proto, @"message (\w+) \{([^}]*)\}");
        Assert.Equal(8, messages.Count);
        var classes = typeof(Account).Assembly.GetTypes()
            .Where(t => t.GetProperties().Any(p => p.GetCustomAttribute<FieldAttribute>() is not null))
            .ToDictionary(t => t.Name);

        foreach (Match message in messages)
        {
            var name = message.Groups[1].Value;
            Assert.True(classes.Remove(name, out var @class), $"No class for the message {name}.");
            var expected = Regex.Matches(message.Groups[2].Value, @"^\s*((?:repeated )?\w+) (\w+) = (\d+);", RegexOptions.Multiline)
                .Select(m => $"{Pascal(m.Groups[2].Value)} = {m.Groups[3].Value} : {m.Groups[1].Value}")
                .Order();
            var found = @class!.GetProperties()
                .Select(p => (Property: p, Field: p.GetCustomAttribute<FieldAttribute>()))
                .Where(x => x.Field is not null)
                .Select(x => $"{x.Property.Name} = {x.Field!.Number} : {ProtoType(x.Property, x.Field)}")
                .Order();
            Assert.Equal(expected, found);
        }
        Assert.Empty(classes.Keys);   // no class without a message in the contract
    }

    [Fact]
    public void The_enumeration_of_the_proto_matches_the_one_of_the_code()
    {
        var values = Regex.Match(Proto, @"enum AccountType \{([^}]*)\}").Groups[1].Value;
        var expected = Regex.Matches(values, @"(\w+) = (\d+);").Select(m => (Pascal(m.Groups[1].Value), int.Parse(m.Groups[2].Value)));
        var found = Enum.GetValues<AccountType>().Select(v => (v.ToString(), (int)v));
        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task The_server_serves_each_method_of_the_service_and_nothing_else()
    {
        await using var system = new TestSystem();
        var expected = Regex.Matches(Proto, @"rpc (\w+) \(").Select(m => $"/{BankGrpcService.Name}/{m.Groups[1].Value}").Order();
        var routes = system.Server.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Where(r => r.StartsWith($"/{BankGrpcService.Name}/"))
            .Order();
        Assert.Equal(expected, routes);
    }
}

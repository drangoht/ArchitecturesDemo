using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Modulaire.Partage;

/// <summary>
/// Ce que chaque module implémente pour se brancher dans l'hôte.
/// L'hôte ne connaît que cette interface : il ignore tout du contenu des modules.
/// </summary>
public interface IModule
{
    string Nom { get; }

    /// <summary>Enregistre les services du module (sa base, ses cas d'utilisation, ses gestionnaires d'événements).</summary>
    void Enregistrer(IServiceCollection services, IConfiguration configuration);

    /// <summary>Déclare les routes HTTP du module : chaque module possède sa propre API.</summary>
    void MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>Initialisation au démarrage (données de démo…). Facultative.</summary>
    Task Initialiser(IServiceProvider services) => Task.CompletedTask;
}

public static class ModuleExtensions
{
    /// <summary>
    /// Nom de la base d'un module. Chaque module a la sienne (en production : un schéma SQL par module).
    /// Lu à la demande pour que les tests puissent isoler chaque exécution.
    /// </summary>
    public static string NomBase(this IServiceProvider services, string module) =>
        $"{services.GetRequiredService<IConfiguration>()["NomBase"] ?? "Banque"}-{module}";
}

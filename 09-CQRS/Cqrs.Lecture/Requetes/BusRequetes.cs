using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Lecture.Requetes;

/// <summary>Une question posée au système. Elle ne modifie rien.</summary>
public interface IRequete<TResultat>;

public interface IGestionnaireRequete<in TRequete, TResultat> where TRequete : IRequete<TResultat>
{
    Task<TResultat> Traiter(TRequete requete);
}

/// <summary>Trouve le gestionnaire de la requête à partir de son type.</summary>
public class BusRequetes(IServiceProvider services)
{
    public Task<TResultat> Demander<TResultat>(IRequete<TResultat> requete)
    {
        var typeGestionnaire = typeof(IGestionnaireRequete<,>).MakeGenericType(requete.GetType(), typeof(TResultat));
        var gestionnaire = services.GetRequiredService(typeGestionnaire);
        return (Task<TResultat>)typeGestionnaire.GetMethod("Traiter")!.Invoke(gestionnaire, [requete])!;
    }
}

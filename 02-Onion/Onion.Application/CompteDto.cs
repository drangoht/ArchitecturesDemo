using Onion.Domain.Model;

namespace Onion.Application;

/// <summary>Ce qui sort de la couche application : jamais l'entité elle-même.</summary>
public record CompteDto(Guid Id, string Titulaire, decimal Solde)
{
    public static CompteDto Depuis(Compte compte) => new(compte.Id, compte.Titulaire, compte.Solde);
}

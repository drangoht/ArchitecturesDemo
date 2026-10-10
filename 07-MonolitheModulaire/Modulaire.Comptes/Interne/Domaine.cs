using Microsoft.EntityFrameworkCore;
using Modulaire.Comptes.Contrats;

namespace Modulaire.Comptes;

internal class Compte(Guid id, string titulaire, decimal solde)
{
    public Guid Id { get; private set; } = id;
    public string Titulaire { get; private set; } = titulaire;
    public decimal Solde { get; private set; } = solde;

    public ResultatOperation Debiter(decimal montant)
    {
        if (montant <= 0)
            return ResultatOperation.Refuse("Le montant doit être strictement positif.");
        if (montant > Solde)
            return ResultatOperation.Refuse("Solde insuffisant.");
        Solde -= montant;
        return ResultatOperation.Ok();
    }

    public ResultatOperation Crediter(decimal montant)
    {
        if (montant <= 0)
            return ResultatOperation.Refuse("Le montant doit être strictement positif.");
        Solde += montant;
        return ResultatOperation.Ok();
    }
}

/// <summary>La base du module Comptes. Aucun autre module n'y accède : ils passent par IComptesApi.</summary>
internal class ComptesDbContext(DbContextOptions<ComptesDbContext> options) : DbContext(options)
{
    public DbSet<Compte> Comptes => Set<Compte>();
}

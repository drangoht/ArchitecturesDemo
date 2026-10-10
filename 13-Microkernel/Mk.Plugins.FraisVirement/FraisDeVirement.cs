using System.Globalization;
using Mk.Contrats;

namespace Mk.Plugins.FraisVirement;

/// <summary>Point d'extension « frais » : un pourcentage du virement, borné par un minimum et un maximum.</summary>
public sealed class FraisDeVirement : IRegleFrais
{
    private decimal _taux = 0.001m, _minimum = 0.20m, _maximum = 5m;

    public string Nom => "FraisDeVirement";
    public string Description => "Frais de virement proportionnels, avec minimum et maximum.";
    public string Libelle => "Frais de virement";

    public void Initialiser(ContextePlugin contexte)
    {
        _taux = Lire(contexte, "taux", _taux);
        _minimum = Lire(contexte, "minimum", _minimum);
        _maximum = Lire(contexte, "maximum", _maximum);
        contexte.Journaliser($"taux {_taux:P2}, entre {_minimum} € et {_maximum} €");
    }

    public decimal CalculerFrais(OperationProposee operation) =>
        operation.Type != TypeOperation.Virement
            ? 0
            : Math.Clamp(Math.Round(operation.Montant * _taux, 2), _minimum, _maximum);

    private static decimal Lire(ContextePlugin contexte, string cle, decimal defaut) =>
        contexte.Reglage(cle) is { } valeur ? decimal.Parse(valeur, CultureInfo.InvariantCulture) : defaut;
}

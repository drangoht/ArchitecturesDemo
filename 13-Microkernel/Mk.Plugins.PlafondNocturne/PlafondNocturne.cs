using System.Globalization;
using Mk.Contrats;

namespace Mk.Plugins.PlafondNocturne;

/// <summary>Point d'extension « contrôle » : retraits limités la nuit (plage et plafond réglables dans plugin.json).</summary>
public sealed class PlafondNocturne : IControleOperation
{
    private decimal _plafond = 200m;
    private TimeOnly _debut = new(22, 0), _fin = new(6, 0);

    public string Nom => "PlafondNocturne";
    public string Description => "Limite le montant des retraits pendant la nuit.";

    public void Initialiser(ContextePlugin contexte)
    {
        if (contexte.Reglage("plafond") is { } plafond)
            _plafond = decimal.Parse(plafond, CultureInfo.InvariantCulture);
        if (contexte.Reglage("debut") is { } debut)
            _debut = TimeOnly.Parse(debut, CultureInfo.InvariantCulture);
        if (contexte.Reglage("fin") is { } fin)
            _fin = TimeOnly.Parse(fin, CultureInfo.InvariantCulture);
        contexte.Journaliser($"retraits limités à {_plafond} € de {_debut:HH\\:mm} à {_fin:HH\\:mm}");
    }

    public string? Verifier(OperationProposee operation)
    {
        if (operation.Type != TypeOperation.Retrait || operation.Montant <= _plafond)
            return null;

        var heure = TimeOnly.FromDateTime(operation.Date);
        var laNuit = _debut <= _fin
            ? heure >= _debut && heure < _fin
            : heure >= _debut || heure < _fin;  // plage à cheval sur minuit
        return laNuit ? $"Retrait limité à {_plafond} € entre {_debut:HH\\:mm} et {_fin:HH\\:mm}." : null;
    }
}

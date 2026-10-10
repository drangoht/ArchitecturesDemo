using Modulaire.Partage;

namespace Modulaire.Virements.Contrats;

/// <summary>
/// L'événement transporte tout ce dont les abonnés ont besoin (y compris les noms des titulaires),
/// pour qu'ils n'aient pas à rappeler le module Comptes : « event-carried state transfer ».
/// </summary>
public record VirementEffectue(
    Guid SourceId, string TitulaireSource,
    Guid CibleId, string TitulaireCible,
    decimal Montant) : IEvenementIntegration;

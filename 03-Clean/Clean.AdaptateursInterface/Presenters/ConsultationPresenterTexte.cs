using System.Globalization;
using Clean.CasUtilisation.Consultation;

namespace Clean.AdaptateursInterface.Presenters;

/// <summary>
/// Deuxième presenter pour le MÊME cas d'utilisation : un relevé texte lisible.
/// C'est l'intérêt de l'output boundary : on ajoute un format sans toucher à l'interacteur.
/// </summary>
public class ConsultationPresenterTexte : IConsultationSortie
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public int Statut { get; private set; } = 200;
    public string Texte { get; private set; } = "";

    public void Succes(ReponseConsultation r) =>
        Texte = $"""
            RELEVÉ DE COMPTE
            Titulaire : {r.Titulaire}
            Solde     : {r.Solde.ToString("N2", Francais)} €
            """;

    public void CompteIntrouvable(Guid compteId)
    {
        Statut = 404;
        Texte = $"Aucun compte ne porte le numéro {compteId}.";
    }
}

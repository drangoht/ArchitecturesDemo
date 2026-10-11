using Ihm.Banque;

namespace Ihm.Presentation;

/// <summary>
/// Le ViewModel de la fenêtre : il assemble les deux parties de l'écran et les relie (un virement réussi actualise
/// les soldes). La vue n'a qu'à le prendre comme DataContext ; elle ne contient aucun code.
/// </summary>
public sealed class EcranPrincipalViewModel
{
    public EcranPrincipalViewModel(IBanque banque, IConfirmation confirmation)
    {
        Comptes = new ComptesViewModel(banque);
        Virement = new VirementViewModel(banque, confirmation, Comptes.Comptes, Comptes.ActualiserAsync);
    }

    public string Titre => "Banque MVVM";

    public ComptesViewModel Comptes { get; }

    public VirementViewModel Virement { get; }

    public Task InitialiserAsync() => Comptes.ActualiserAsync();
}

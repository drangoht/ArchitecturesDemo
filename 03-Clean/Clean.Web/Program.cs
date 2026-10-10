using Clean.AdaptateursInterface.Controleurs;
using Clean.AdaptateursInterface.Passerelles;
using Clean.AdaptateursInterface.Presenters;
using Clean.CasUtilisation.Consultation;
using Clean.CasUtilisation.Passerelles;
using Clean.CasUtilisation.Retrait;
using Clean.CasUtilisation.Virement;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IComptesPasserelle>(ComptesPasserelleEnMemoire.AvecDonneesDeDemo());
builder.Services.AddScoped<IConsultationEntree, ConsultationInteracteur>();
builder.Services.AddScoped<IRetraitEntree, RetraitInteracteur>();
builder.Services.AddScoped<IVirementEntree, VirementInteracteur>();
builder.Services.AddScoped<ComptesControleur>();

var app = builder.Build();

// Chaque route : créer le presenter → appeler le contrôleur → écrire la vue produite par le presenter.

app.MapGet("/comptes/{id:guid}", async (Guid id, string? format, ComptesControleur controleur) =>
{
    if (format == "texte")
    {
        var texte = new ConsultationPresenterTexte();
        await controleur.Consulter(id, texte);
        return Results.Text(texte.Texte, "text/plain; charset=utf-8", statusCode: texte.Statut);
    }

    var presenter = new ConsultationPresenterHttp();
    await controleur.Consulter(id, presenter);
    return EnHttp(presenter.Vue);
});

app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, RetraitRequete requete, ComptesControleur controleur) =>
{
    var presenter = new RetraitPresenterHttp();
    await controleur.Retirer(id, requete.Montant, presenter);
    return EnHttp(presenter.Vue);
});

app.MapPost("/virements", async (VirementRequete requete, ComptesControleur controleur) =>
{
    var presenter = new VirementPresenterHttp();
    await controleur.Virer(requete.Source, requete.Cible, requete.Montant, presenter);
    return EnHttp(presenter.Vue);
});

app.Run();

static IResult EnHttp(VueHttp vue) => Results.Json(vue.Corps, statusCode: vue.Statut);

record RetraitRequete(decimal Montant);
record VirementRequete(Guid Source, Guid Cible, decimal Montant);

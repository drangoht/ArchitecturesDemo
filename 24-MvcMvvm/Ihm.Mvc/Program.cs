using Ihm.Banque;
using Microsoft.AspNetCore.Mvc;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBanque>(s => new BanqueEnMemoire(s.GetRequiredService<TimeProvider>()));

builder.Services.AddControllersWithViews(options =>
{
    // Tout formulaire envoyé en POST doit porter le jeton anti-falsification que le tag helper <form> ajoute tout seul.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

    // Les messages de la liaison de modèle (saisie qui n'est pas un nombre…), en français.
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((valeur, _) => $"« {valeur} » n'est pas un montant valide.");
    messages.SetValueMustNotBeNullAccessor(_ => "Ce champ est obligatoire.");
    messages.SetValueMustBeANumberAccessor(_ => "Saisissez un nombre.");
});

// Razor encode par défaut tout ce qui sort de l'ASCII (« é » devient « &#xE9; ») : on garde le HTML lisible.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
    options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

var app = builder.Build();

// Les montants se saisissent et s'affichent à la française (« 250,50 »), quelle que soit la langue du navigateur.
app.UseRequestLocalization("fr-FR");
app.UseRouting();

// La route conventionnelle : /{contrôleur}/{action}/{id?}, et par défaut la liste des comptes.
app.MapControllerRoute("defaut", "{controller=Comptes}/{action=Index}/{id?}");

app.Run();

namespace Ihm.Mvc
{
    /// <summary>Repère pour la fabrique de tests (WebApplicationFactory).</summary>
    public sealed class Marqueur;
}

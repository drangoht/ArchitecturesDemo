using Ui.Bank;
using Microsoft.AspNetCore.Mvc;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBank>(s => new InMemoryBank(s.GetRequiredService<TimeProvider>()));

builder.Services.AddControllersWithViews(options =>
{
    // Any form sent with POST must carry the anti-forgery token that the <form> tag helper adds by itself.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

    // The model binding messages (input that is not a number…), worded for the user.
    var messages = options.ModelBindingMessageProvider;
    messages.SetAttemptedValueIsInvalidAccessor((value, _) => $"\"{value}\" is not a valid amount.");
    messages.SetValueMustNotBeNullAccessor(_ => "This field is required.");
    messages.SetValueMustBeANumberAccessor(_ => "Enter a number.");
});

// Razor encodes by default everything outside ASCII ("€" becomes "&#x20AC;"): we keep the HTML readable.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
    options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

var app = builder.Build();

// The amounts are typed and displayed the British way ("250.50"), whatever the browser's language.
app.UseRequestLocalization("en-GB");
app.UseRouting();

// The conventional route: /{controller}/{action}/{id?}, and by default the list of accounts.
app.MapControllerRoute("default", "{controller=Accounts}/{action=Index}/{id?}");

app.Run();

namespace Ui.Mvc
{
    /// <summary>Marker for the test factory (WebApplicationFactory).</summary>
    public sealed class Marker;
}

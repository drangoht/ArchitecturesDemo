using Couches.Metier;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCoucheMetier();

var app = builder.Build();
await app.Services.PreparerDonneesDeDemo();

app.MapControllers();

app.Run();

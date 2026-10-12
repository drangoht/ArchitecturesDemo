using Sls.Functions;
using Sls.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddServerlessPlatform(Deployment.Declare);

var app = builder.Build();

DemoData.Load(app.Services.GetRequiredService<ManagedServices>());

app.MapGet("/", () => Results.Redirect("/platform/functions"));
app.MapServerlessPlatform();

app.Run();

public partial class Program;

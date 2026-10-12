using Layered.Business;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddBusinessLayer();

var app = builder.Build();
await app.Services.PrepareDemoData();

app.MapControllers();

app.Run();

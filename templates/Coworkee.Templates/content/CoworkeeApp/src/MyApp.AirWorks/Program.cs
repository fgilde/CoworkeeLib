using Coworkee.Bff;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// signs in at the auth server like the web app (a client of its own) and forwards /api, /odata and /hubs with the access token
builder.AddCoworkeeBff();

// uploads stream through to the api, so the host takes bodies as large as the api does
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 101 * 1024 * 1024);

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapCoworkeeBff();
app.MapFallbackToFile("index.html");
app.Run();

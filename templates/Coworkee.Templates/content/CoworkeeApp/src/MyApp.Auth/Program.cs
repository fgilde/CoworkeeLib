using Coworkee.AspNetCore;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Coworkee.Settings;
using MyApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Configuration.AddCoworkeeDatabaseConfiguration(MyAppInfrastructureModule.ConnectionStringName);
builder.AddCoworkee<MyAppAuthModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

[DependsOn(typeof(MyAppInfrastructureModule), typeof(CoworkeeAuthServerModule))]
internal sealed class MyAppAuthModule : CoworkeeModule;

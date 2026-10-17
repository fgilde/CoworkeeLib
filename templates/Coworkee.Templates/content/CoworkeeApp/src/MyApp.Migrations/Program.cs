using Coworkee.Core.Modularity;
using Coworkee.Identity.Setup;
using MyApp.Infrastructure;
using MyApp.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<MyAppDatabaseModule>(builder.Configuration);
builder.AddCoworkeeMigrations<MyAppDbContext>(options =>
{
    options.DevelopmentSeed = DemoSeed.Configure;
    options.DeploymentSeed = DemoSeed.ConfigureDeployment;
});
#if (samples)
builder.Services.AddScoped<Coworkee.Application.Setup.ISetupStep, DemoData>();
#endif
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();

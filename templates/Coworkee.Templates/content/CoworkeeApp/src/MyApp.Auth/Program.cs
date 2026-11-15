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

#if (samples)
// registration documents go to the documents of the app
[DependsOn(typeof(MyAppInfrastructureModule), typeof(CoworkeeAuthServerModule), typeof(MyApp.Documents.Registration.MyAppRegistrationDocumentsModule))]
#elif (files)
// registration documents go to the files (Registrations/{user}), without the files api
[DependsOn(typeof(MyAppInfrastructureModule), typeof(CoworkeeAuthServerModule), typeof(Coworkee.Files.CoworkeeFileRegistrationDocumentsModule))]
#else
[DependsOn(typeof(MyAppInfrastructureModule), typeof(CoworkeeAuthServerModule))]
#endif
internal sealed class MyAppAuthModule : CoworkeeModule;

#if (storage == "azureBlob" || ai)
using Coworkee.Aspire.Settings;

#endif
#if (keycloak || grafana || webDataStudio)
using MyApp.Migrations;

#endif
var builder = DistributedApplication.CreateBuilder(args);
#if (translationProvider != "libreTranslate" && translationProvider != "none" && translationProvider != "ai")

// key, Azure region and MyMemory contact of the translation service optionally from the user secrets (Parameters:translation-key, translation-region, translation-email)
var translation = new[] { "key", "region", "email" }.Where(n => builder.Configuration[$"Parameters:translation-{n}"] is { Length: > 0 })
    .ToDictionary(n => n, n => builder.AddParameter($"translation-{n}", secret: n == "key"));
#endif

var app = builder.AddCoworkeeApp("myapp", options =>
{
    options.DisplayName = "COWORKEE_APP_TITLE";
    options.LogoUrl = "/coworkee-icon.svg";
#if (customSignIn)
    options.SignIn.Layout = "COWORKEE_SIGNIN_LAYOUT";
    options.SignIn.MediaSide = Coworkee.Contracts.Theming.SignInMediaSide.COWORKEE_SIGNIN_SIDE;
#endif
#if (signInMedia != "")
    options.SignIn.MediaUrl = "COWORKEE_SIGNIN_MEDIA";
#endif
#if (!redis)
    options.WithoutRedis();
#endif
#if (database == "sqlServer")
    options.UseSqlServer();
#elif (database == "sqlite")
    options.UseSqlite();
#endif
#if (grafana || webDataStudio)
    // Grafana and WebDataStudio take the sign-in of the app's administrator (LOGIN.md)
    var administrator = DemoUsers.All.First(u => u.IsAdmin);
    options.Administrator = new(administrator.Email, administrator.Password);
#endif
#if (grafana)
    options.UseGrafana();
#endif
#if (webDataStudio)
    options.UseWebDataStudio();
#endif
#if (translationSource != "en")
    options.TranslationSource = "COWORKEE_TRANSLATION_SOURCE";
#endif
#if (translationProvider == "libreTranslate")
    // free machine translation without an account; the first start downloads the models of the app's languages
#if (languages != "")
    options.UseLibreTranslate(["COWORKEE_LANGUAGES", "COWORKEE_DEFAULT_LANGUAGE"]);
#else
    options.UseLibreTranslate(["de", "COWORKEE_DEFAULT_LANGUAGE"]);
#endif
#elif (translationProvider == "none" || translationProvider == "ai")
    options.UseTranslation("COWORKEE_TRANSLATION_PROVIDER");
#else
    options.UseTranslation("COWORKEE_TRANSLATION_PROVIDER", translation.GetValueOrDefault("key"), translation.GetValueOrDefault("region"), translation.GetValueOrDefault("email"));
#endif
#if (keycloak)
    // the same users and passwords as the app's seed (LOGIN.md)
    options.UseKeycloak(keycloak => keycloak.Users.AddRange(DemoUsers.All.Select(u => new KeycloakUser(u.Email, u.FirstName, u.LastName ?? "COWORKEE_APP_TITLE") { Password = u.Password, Roles = u.IsAdmin ? ["Administrator", .. u.Roles ?? []] : u.Roles ?? [] })));
#endif
});
#if (storage == "azureBlob")

var blobs = builder.AddAzureStorage("storage").RunAsEmulator().AddBlobs("blobs");
app.Modules[Coworkee.Aspire.Modules.CoworkeeModules.Storage] = (_, service) => service.WithReference(blobs).WaitFor(blobs)
    .WithSetting(s => s.Coworkee.Storage.Provider, "AzureBlob")
    .WithSetting(s => s.Coworkee.Storage.AzureBlob.ConnectionString, blobs.Resource.ConnectionStringExpression);
#endif
#if (eventBus == "rabbitMq")

var rabbitMq = builder.AddRabbitMQ("rabbitmq");
app.Modules.Add("Coworkee.EventBus.RabbitMq", (_, service) => service.WithReference(rabbitMq).WaitFor(rabbitMq));
#endif
#if (ai)

// provider defaults of the AI settings; key, endpoint and model optionally from the user secrets (Parameters:ai-apikey, ai-endpoint, ai-model)
var aiParameters = new[] { "ApiKey", "Endpoint", "Model" }.Where(n => builder.Configuration[$"Parameters:ai-{n.ToLowerInvariant()}"] is { Length: > 0 })
    .ToDictionary(n => n, n => builder.AddParameter($"ai-{n.ToLowerInvariant()}", secret: n == "ApiKey"));
#if (aiProvider == "ollama")
var ollama = builder.AddOllama("ollama").WithDataVolume();
ollama.AddModel("llama3.2");
#endif
app.Modules.Add("Coworkee.Ai", (_, service) =>
{
    service.WithSetting(s => s.Coworkee.Settings.Defaults["Ai.Provider"], "COWORKEE_AI_PROVIDER");
    foreach (var (name, parameter) in aiParameters)
    {
        service.WithSetting(s => s.Coworkee.Settings.Defaults[$"Ai.{name}"], parameter);
    }
#if (aiProvider == "ollama")
    service.WithReference(ollama)
        .WithSetting(s => s.Coworkee.Settings.Defaults["Ai.Endpoint"], ollama.GetEndpoint("http"))
        .WithSetting(s => s.Coworkee.Settings.Defaults["Ai.Enabled"], true);
#else
    if (aiParameters.ContainsKey("ApiKey"))
    {
        service.WithSetting(s => s.Coworkee.Settings.Defaults["Ai.Enabled"], true);
    }
#endif
});
#endif

app.AddMigrations<Projects.MyApp_Migrations>();
app.AddAuthServer<Projects.MyApp_Auth>();
app.AddApi<Projects.MyApp_Api>();
app.AddWeb<Projects.MyApp_Web>();

builder.Build().Run();

#if (storage == "azureBlob")
using Coworkee.Aspire.Settings;

#endif
var builder = DistributedApplication.CreateBuilder(args);

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
#if (keycloak)
    options.UseKeycloak(keycloak => keycloak.Users.Add(new KeycloakUser("admin@myapp.local", "Administrator", "COWORKEE_APP_TITLE")));
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

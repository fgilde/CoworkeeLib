using Aspire.Hosting.ApplicationModel;
using CommunityToolkit.Aspire.Hosting.MailPit;
using Coworkee.Aspire.Modules;
using Coworkee.Aspire.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aspire.Hosting;

/// <summary>
/// A Coworkee application in the app host. Add the services in the order migrations, auth server, apis and workers, web;
/// each one gets the infrastructure its Coworkee packages need (database, Redis, mail, search, file storage) and the settings that connect them.
/// </summary>
public sealed class CoworkeeApp
{
    private const string Health = "/health";
    private readonly List<IResourceBuilder<ProjectResource>> _apis = [];
    private IResourceBuilder<RedisResource>? _redis;
    private IResourceBuilder<MailPitContainerResource>? _mail;
    private IResourceBuilder<ElasticsearchResource>? _search;

    internal CoworkeeApp(IDistributedApplicationBuilder builder, string name, CoworkeeAppOptions options)
    {
        (Builder, Name, Options) = (builder, name, options);
        Server = builder.AddPostgres("postgres");
        if (Persistent)
        {
            Server.WithDataVolume();
        }

        Database = Server.AddDatabase(name);
    }

    public IDistributedApplicationBuilder Builder { get; }

    public string Name { get; }

    public CoworkeeAppOptions Options { get; }

    /// <summary>Wait for the server, not the database: the migrations create a missing database, and Aspire 13.6 lost the database state after restarts.</summary>
    public IResourceBuilder<PostgresServerResource> Server { get; }

    public IResourceBuilder<PostgresDatabaseResource> Database { get; }

    public IResourceBuilder<ProjectResource>? Migrations { get; private set; }

    public IResourceBuilder<ProjectResource>? AuthServer { get; private set; }

    public IResourceBuilder<ProjectResource>? Web { get; private set; }

    public IResourceBuilder<KeycloakResource>? Keycloak { get; private set; }

    public IReadOnlyList<IResourceBuilder<ProjectResource>> Apis => _apis;

    public string ApiAudience => $"{Name}_api";

    private bool Persistent => !Builder.Configuration.GetValue<bool>(CoworkeeAppExtensions.EphemeralSetting);

    private string BlobRoot => Options.BlobRoot ?? Path.GetFullPath(Path.Combine(Builder.AppHostDirectory, "..", "..", ".data", "blobs"));

    public IResourceBuilder<RedisResource> Redis => _redis ??= AddRedis();

    public IResourceBuilder<MailPitContainerResource> Mail => _mail ??= Builder.AddMailPit("mail");

    // a small heap keeps development machines responsive and nearly full dev disks do not block shards; the C2 JIT of the bundled JDK crashed (SIGSEGV in PhaseChaitin), so C1 only
    public IResourceBuilder<ElasticsearchResource> Search => _search ??= AddSearch();

    public IResourceBuilder<ProjectResource> AddMigrations<TProject>(string suffix = "migrations")
        where TProject : IProjectMetadata, new()
    {
        Migrations = Builder.AddProject<TProject>($"{Name}-{suffix}").WithReference(Database).WaitFor(Server);
        return Migrations;
    }

    public IResourceBuilder<ProjectResource> AddAuthServer<TProject>(string suffix = "auth")
        where TProject : IProjectMetadata, new()
    {
        var auth = Service<TProject>(suffix).WithExternalHttpEndpoints();
        AuthServer = auth;
        auth.WithSetting(s => s.Coworkee.Jobs.RunServer, false)
            .WithSetting(s => s.Coworkee.Account.PublicAuthUrl, auth.GetEndpoint("https"))
            .WithSetting(s => s.Coworkee.Auth.DisplayName, Options.DisplayName ?? Name);
        if (Builder.Environment.IsDevelopment())
        {
            // per machine certificates are fine locally; production configures SigningCertificate and EncryptionCertificate
            auth.WithSetting(s => s.Coworkee.Auth.DevelopmentCertificates, true);
        }

        if (Options.Keycloak is { } keycloak)
        {
            AddKeycloak(auth, keycloak);
        }

        return auth;
    }

    public IResourceBuilder<ProjectResource> AddApi<TProject>(string suffix = "api")
        where TProject : IProjectMetadata, new()
    {
        var auth = Require(AuthServer, nameof(AddAuthServer), nameof(AddApi));
        var api = Service<TProject>(suffix).WithExternalHttpEndpoints()
            .WithSetting(s => s.Coworkee.ApiAuth.Authority, auth.GetEndpoint("https"))
            .WithSetting(s => s.Coworkee.ApiAuth.Audience, ApiAudience)
            .WithSetting(s => s.Coworkee.Account.PublicAuthUrl, auth.GetEndpoint("https"));
        if (Builder.Configuration["Coworkee:SetupToken"] is { Length: > 0 } setupToken)
        {
            api.WithSetting(s => s.Coworkee.SetupToken, setupToken);
        }

        auth.WithSetting(s => s.Coworkee.Auth.ApiScopes[ApiAudience], ApiAudience);
        _apis.Add(api);
        return api;
    }

    /// <summary>A background service: same infrastructure as an api, no public endpoints.</summary>
    public IResourceBuilder<ProjectResource> AddWorker<TProject>(string suffix)
        where TProject : IProjectMetadata, new() => Service<TProject>(suffix);

    public IResourceBuilder<ProjectResource> AddWeb<TProject>(string suffix = "web")
        where TProject : IProjectMetadata, new()
    {
        var auth = Require(AuthServer, nameof(AddAuthServer), nameof(AddWeb));
        var api = Require(_apis.FirstOrDefault(), nameof(AddApi), nameof(AddWeb));
        var clientId = $"{Name}-{suffix}";
        var web = Service<TProject>(suffix).WithExternalHttpEndpoints()
            .WithReference(api)
            .WaitFor(auth)
            .WithSetting(s => s.Coworkee.Bff.Authority, auth.GetEndpoint("https"))
            .WithSetting(s => s.Coworkee.Bff.ClientId, clientId)
            .WithSetting(s => s.Coworkee.Bff.ApiAddress, $"https+http://{api.Resource.Name}")
            .WithSettings(s => s.Coworkee.Bff.Scopes, ApiAudience)
            .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/admin/jobs", "/hubs");
        Web = web;

        var redirect = web.GetEndpoint("https");
        auth.WithSetting(s => s.Coworkee.Auth.Clients[0].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.Clients[0].DisplayName, Options.DisplayName ?? Name)
            .WithSettings(s => s.Coworkee.Auth.Clients[0].Scopes, ApiAudience)
            .WithSetting(s => s.Coworkee.Auth.Clients[0].RedirectUris[0], ReferenceExpression.Create($"{redirect}/signin-oidc"))
            .WithSetting(s => s.Coworkee.Auth.Clients[0].PostLogoutRedirectUris[0], ReferenceExpression.Create($"{redirect}/signout-callback-oidc"));
        foreach (var service in _apis.Where(a => CoworkeeModules.Of(a.Resource).Contains(CoworkeeModules.Notifications)))
        {
            // links in digest mails lead to the web app
            service.WithSetting(s => s.Coworkee.Notifications.PublicAppUrl, redirect);
        }

        return web;
    }

    private IResourceBuilder<ProjectResource> Service<TProject>(string suffix)
        where TProject : IProjectMetadata, new()
    {
        var service = Builder.AddProject<TProject>($"{Name}-{suffix}").WithHttpHealthCheck(Health);
        var modules = CoworkeeModules.Of(service.Resource);
        if (modules.Contains(CoworkeeModules.Infrastructure))
        {
            service.WithReference(Database);
            _ = Migrations is { } migrations ? service.WaitForCompletion(migrations) : service.WaitFor(Server);
        }

        if (modules.Contains(CoworkeeModules.Realtime))
        {
            service.WithReference(Redis).WaitFor(Redis);
        }

        if (modules.Contains(CoworkeeModules.Mailing))
        {
            var smtp = Mail.GetEndpoint("smtp");
            service.WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"], ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Host)}"))
                .WithSetting(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Port"], ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Port)}"))
                .WaitFor(Mail);
        }

        if (modules.Contains(CoworkeeModules.Storage))
        {
            service.WithSetting(s => s.Coworkee.Storage.FileSystem.Root, BlobRoot);
        }

        if (modules.Contains(CoworkeeModules.Search))
        {
            // search may start later: the index is created on first use and index jobs retry
            service.WithReference(Search);
        }

        return service;
    }

    private void AddKeycloak(IResourceBuilder<ProjectResource> auth, CoworkeeKeycloakOptions options)
    {
        var realm = options.Realm ?? Name;
        var clientId = $"{Name}-auth";
        var secret = Builder.AddParameter($"{Name}-keycloak-client-secret", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
        var password = Builder.AddParameter($"{Name}-keycloak-user-password", new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true, persist: true);
        var import = KeycloakRealm.Write(Path.Combine(Builder.AppHostDirectory, "obj", "keycloak"), realm, clientId, options.Users);

        Keycloak = Builder.AddKeycloak("keycloak", options.Port)
            .WithRealmImport(import)
            .WithEnvironment(KeycloakRealm.ClientSecretVariable, secret)
            .WithEnvironment(KeycloakRealm.UserPasswordVariable, password);
        if (Persistent)
        {
            Keycloak.WithDataVolume();
        }

        const string Provider = "keycloak";
        auth.WaitFor(Keycloak)
            .WithSetting(s => s.Coworkee.Auth.External.Mode, options.LoginMode.ToString())
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].DisplayName, options.DisplayName)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].Authority, ReferenceExpression.Create($"{Keycloak.GetEndpoint("http")}/realms/{realm}"))
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].ClientSecret, secret)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].RequireHttpsMetadata, false);
    }

    private IResourceBuilder<RedisResource> AddRedis()
    {
        var redis = Builder.AddRedis("redis");
        return Persistent ? redis.WithDataVolume() : redis;
    }

    private IResourceBuilder<ElasticsearchResource> AddSearch()
    {
        var search = Builder.AddElasticsearch("elasticsearch")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m -XX:TieredStopAtLevel=1")
            .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false");
        return Persistent ? search.WithDataVolume() : search;
    }

    private static T Require<T>(T? resource, string add, string caller)
        where T : class =>
        resource ?? throw new InvalidOperationException($"Call {add} before {caller}.");
}

using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Modules;
using Coworkee.Aspire.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aspire.Hosting;

/// <summary>
/// A Coworkee application in the app host. Add the services in the order migrations, auth server, apis and workers, web (or all at once with <see cref="AddProjects"/>);
/// each one gets the infrastructure its Coworkee packages need (database, Redis, mail, search, file storage) and the settings that connect them.
/// </summary>
public sealed partial class CoworkeeApp
{
    private const string Health = "/health";
    private readonly List<IResourceBuilder<ProjectResource>> _apis = [];
    private readonly HashSet<Type> _added = [];

    internal CoworkeeApp(IDistributedApplicationBuilder builder, string name, CoworkeeAppOptions options)
    {
        (Builder, Name, Options) = (builder, name, options);
        Modules = BuiltInModules();
        foreach (var module in options.Disabled)
        {
            Modules.Remove(module);
        }

        if (options.DockerCompose is { } compose)
        {
            compose(builder.AddDockerComposeEnvironment("compose"));
        }
    }

    public IDistributedApplicationBuilder Builder { get; }

    public string Name { get; }

    public CoworkeeAppOptions Options { get; }

    /// <summary>Wiring by package or project name: a service that references the name gets it, e.g. <c>app.Modules.Add("MyApp.Billing", (app, service) =&gt; service.WithReference(stripe))</c>.</summary>
    public OrderedDictionary<string, Action<CoworkeeApp, IResourceBuilder<ProjectResource>>> Modules { get; }

    public IResourceBuilder<ProjectResource>? Migrations { get; private set; }

    public IResourceBuilder<ProjectResource>? AuthServer { get; private set; }

    public IResourceBuilder<ProjectResource>? Web { get; private set; }

    public IResourceBuilder<KeycloakResource>? Keycloak { get; private set; }

    public IReadOnlyList<IResourceBuilder<ProjectResource>> Apis => _apis;

    public string ApiAudience => $"{Name}_api";

    private bool Persistent => !Builder.Configuration.GetValue<bool>(CoworkeeAppExtensions.EphemeralSetting);

    private bool LocalDevelopment => Builder.ExecutionContext.IsRunMode && Builder.Environment.IsDevelopment();

    public IResourceBuilder<ProjectResource> AddMigrations<TProject>(string suffix = "migrations")
        where TProject : IProjectMetadata, new()
    {
        Migrations = Builder.AddProject<TProject>($"{Name}-{suffix}").WithReference(AppDatabase, Name);
        _ = Options.Database is null ? Migrations.WaitFor(Server) : Migrations;
        AddDatabaseCommands(Migrations);
        return Added<TProject>(Migrations, suffix);
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

        return Added<TProject>(auth, suffix);
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
        return Added<TProject>(api, suffix);
    }

    /// <summary>A background service: same infrastructure as an api, no public endpoints.</summary>
    public IResourceBuilder<ProjectResource> AddWorker<TProject>(string suffix)
        where TProject : IProjectMetadata, new() => Added<TProject>(Service<TProject>(suffix), suffix);

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
        AddWebUrls(web);

        var redirect = web.GetEndpoint("https");
        auth.WithSetting(s => s.Coworkee.Auth.Clients[0].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.Clients[0].DisplayName, Options.DisplayName ?? Name)
            .WithSettings(s => s.Coworkee.Auth.Clients[0].Scopes, ApiAudience)
            .WithSetting(s => s.Coworkee.Auth.Clients[0].RedirectUris[0], ReferenceExpression.Create($"{redirect}/signin-oidc"))
            .WithSetting(s => s.Coworkee.Auth.Clients[0].PostLogoutRedirectUris[0], ReferenceExpression.Create($"{redirect}/signout-callback-oidc"));
        return Added<TProject>(web, suffix);
    }

    private IResourceBuilder<ProjectResource> Service<TProject>(string suffix)
        where TProject : IProjectMetadata, new()
    {
        var service = Builder.AddProject<TProject>($"{Name}-{suffix}").WithHttpHealthCheck(Health);
        var references = CoworkeeModules.References(service.Resource.GetProjectMetadata().ProjectPath);
        foreach (var (_, wire) in Modules.Where(m => references.Contains(m.Key)).ToList())
        {
            wire(this, service);
        }

        return service;
    }

    private IResourceBuilder<ProjectResource> Added<TProject>(IResourceBuilder<ProjectResource> service, string suffix)
    {
        _added.Add(typeof(TProject));
        Options.Services.GetValueOrDefault(suffix)?.Invoke(service);
        return service;
    }

    private static T Require<T>(T? resource, string add, string caller)
        where T : class =>
        resource ?? throw new InvalidOperationException($"Call {add} before {caller}.");
}

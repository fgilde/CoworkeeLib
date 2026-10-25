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
    private int _bffClients;

    internal CoworkeeApp(IDistributedApplicationBuilder builder, string name, CoworkeeAppOptions options)
    {
        (Builder, Name, Options) = (builder, name, options);
        Modules = BuiltInModules();
        foreach (var module in options.Disabled)
        {
            Modules.Remove(module);
        }

        AddDeployTarget();
        AddTools();
    }

    public IDistributedApplicationBuilder Builder { get; }

    public string Name { get; }

    public CoworkeeAppOptions Options { get; }

    /// <summary>Wiring by package or project name: a service that references the name gets it, e.g. <c>app.Modules.Add("MyApp.Billing", (app, service) =&gt; service.WithReference(stripe))</c>.</summary>
    public OrderedDictionary<string, Action<CoworkeeApp, IResourceBuilder<ProjectResource>>> Modules { get; }

    public IResourceBuilder<ProjectResource>? Migrations { get; private set; }

    public IResourceBuilder<ProjectResource>? AuthServer { get; private set; }

    public IResourceBuilder<ProjectResource>? Web { get; private set; }

    public IResourceBuilder<ProjectResource>? AirWorks { get; private set; }

    public IResourceBuilder<ProjectResource>? ElsaStudio { get; private set; }

    public IResourceBuilder<KeycloakResource>? Keycloak { get; private set; }

    public IReadOnlyList<IResourceBuilder<ProjectResource>> Apis => _apis;

    public string ApiAudience => $"{Name}_api";

    private bool Persistent => !Builder.Configuration.GetValue<bool>(CoworkeeAppExtensions.EphemeralSetting);

    private bool LocalDevelopment => Builder.ExecutionContext.IsRunMode && Builder.Environment.IsDevelopment();

    public IResourceBuilder<ProjectResource> AddMigrations<TProject>(string suffix = "migrations")
        where TProject : IProjectMetadata, new()
    {
        Migrations = WithAppDatabase(Builder.AddProject<TProject>($"{Name}-{suffix}"), awaitServer: true);
        AsDeployJob(Migrations);
        WithInitialAdmin(Migrations);
        if (LocalDevelopment)
        {
            // the migrations project has no launch profile and would run as Production: no seed of the development users, so a new app showed its setup page
            Migrations.WithEnvironment("DOTNET_ENVIRONMENT", Builder.Environment.EnvironmentName)
                .WithEnvironment("Coworkee__Identity__SyncDevelopmentUsers", "true");
        }

        WireGrafanaTelemetry(Migrations);
        // the seed writes through the modules as well (files into the blob storage, ...); the database is wired above
        WireModules(Migrations, except: CoworkeeModules.Infrastructure);
        AddDatabaseCommands(Migrations);
        return Added<TProject>(Migrations, suffix);
    }

    public IResourceBuilder<ProjectResource> AddAuthServer<TProject>(string suffix = "auth")
        where TProject : IProjectMetadata, new()
    {
        var auth = Exposed(Service<TProject>(suffix));
        AuthServer = auth;
        auth.WithSetting(s => s.Coworkee.Jobs.RunServer, false)
            .WithSetting(s => s.Coworkee.Account.PublicAuthUrl, PublicUrl(auth))
            .WithSetting(s => s.Coworkee.Auth.DisplayName, Options.DisplayName ?? Name);
        // a path is a file address on Linux, only http(s) logos reach the auth server
        if (Uri.TryCreate(Options.LogoUrl, UriKind.Absolute, out var logo) && logo.Scheme is "https" or "http")
        {
            auth.WithSetting(s => s.Coworkee.Auth.LogoUrl, Options.LogoUrl!);
        }
        WithSignInDesign(auth);
        if (Publishing)
        {
            WithProductionAuth(auth);
        }
        else if (Builder.Environment.IsDevelopment())
        {
            // per machine certificates are fine locally; production configures SigningCertificate and EncryptionCertificate
            auth.WithSetting(s => s.Coworkee.Auth.DevelopmentCertificates, true);
        }

        AddKibanaSignIn();

        // the running app host reconciles the realm with the development users, the migrations of a deployment the one with its administrator
        if (Options.Keycloak is { } keycloak && Deploys(CoworkeeTools.Keycloak))
        {
            AddKeycloak(auth, keycloak);
            AddToolsSignIn();
        }

        return Added<TProject>(auth, suffix);
    }

    public IResourceBuilder<ProjectResource> AddApi<TProject>(string suffix = "api")
        where TProject : IProjectMetadata, new()
    {
        var auth = Require(AuthServer, nameof(AddAuthServer), nameof(AddApi));
        var api = Exposed(Service<TProject>(suffix))
            .WithSetting(s => s.Coworkee.ApiAuth.Authority, PublicUrl(auth))
            .WithSetting(s => s.Coworkee.ApiAuth.Audience, ApiAudience)
            .WithSetting(s => s.Coworkee.Account.PublicAuthUrl, PublicUrl(auth));
        if (Publishing && Builder.Configuration["Coworkee:SetupToken"] is { Length: > 0 } setupToken)
        {
            api.WithSetting(s => s.Coworkee.SetupToken, setupToken);
        }
        else if (Publishing)
        {
            api.WithSetting(s => s.Coworkee.SetupToken, DeployParameter($"{Name}-setup-token", secret: true));
        }

        AnnounceSetup(api);

        AddKeycloakSignInSettings(api);
        WithSignInDesign(api);
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
        var web = BffClient<TProject>(suffix, Title, nameof(AddWeb))
            .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/admin/jobs", "/hubs")
            .WithSetting(s => s.Coworkee.Bff.Pwa.Name, Title);
        if (Options.LogoUrl is { } icon && icon.StartsWith('/'))
        {
            web.WithSetting(s => s.Coworkee.Bff.Pwa.Icon, icon);
        }

        Web = web;
        AddWebUrls(web);

        var redirect = PublicUrl(web);
        if (Options.LogoUrl is { } logo && logo.StartsWith('/'))
        {
            auth.WithSetting(s => s.Coworkee.Auth.LogoUrl, ReferenceExpression.Create($"{redirect}{logo}"));
        }

        foreach (var service in _apis.Prepend(auth))
        {
            if (Options.SignIn.MediaUrl is { } media && media.StartsWith('/'))
            {
                service.WithSetting(s => s.Coworkee.Auth.Design.MediaUrl, ReferenceExpression.Create($"{redirect}{media}"));
            }
        }

        foreach (var service in _loginTheme is null ? [] : _apis)
        {
            service.WithSetting(s => s.Coworkee.Keycloak.BaseUrl, redirect);
        }

        return Added<TProject>(web, suffix);
    }

    /// <summary>
    /// The AirWorks web desktop (<c>MyApp.AirWorks</c>): a second client of the same auth server and api, with its own session cookie because
    /// browsers send cookies to every port of localhost. Add it after the api.
    /// </summary>
    public IResourceBuilder<ProjectResource> AddAirWorks<TProject>(string suffix = "airworks")
        where TProject : IProjectMetadata, new()
    {
        var airWorks = BffClient<TProject>(suffix, $"{Title} (AirWorks)", nameof(AddAirWorks))
            .WithSettings(s => s.Coworkee.Bff.ForwardedPrefixes, "/hubs")
            .WithSetting(s => s.Coworkee.Bff.CookieName, $"{Name}-{suffix}");
        if (Web is not null)
        {
            // the desktop shows pages of the web client in windows (embedApp, links of notifications)
            airWorks.WithSetting(s => s.Coworkee.Bff.WebAppUrl, PublicUrl(Web));
            Web.WithSetting(s => s.Coworkee.Bff.FrameAncestors[0], PublicUrl(airWorks));
        }

        AirWorks = airWorks;
        return Added<TProject>(airWorks, suffix);
    }

    /// <summary>
    /// Elsa Studio, the workflow designer (<c>MyApp.ElsaStudio</c> with Coworkee.Workflows.Elsa.Studio): a client of its own of the auth server that calls
    /// the Elsa API of the first api with the user's token. The apis' <c>Coworkee:Workflows:Elsa:StudioUrl</c> (the designer link) leads there. Add it after the api.
    /// </summary>
    public IResourceBuilder<ProjectResource> AddElsaStudio<TProject>(string suffix = "elsastudio")
        where TProject : IProjectMetadata, new()
    {
        var studio = BffClient<TProject>(suffix, $"{Title} (Workflow designer)", nameof(AddElsaStudio))
            .WithSetting(s => s.Coworkee.Bff.CookieName, $"{Name}-{suffix}");
        // TLS ends at the proxy in a deployment, the services talk plain http to each other there
        var api = _apis[0];
        studio.WithEnvironment(ElsaStudioBackendUrl, ReferenceExpression.Create($"{api.GetEndpoint(Publishing ? "http" : "https")}/api/elsa"));
        foreach (var service in _apis)
        {
            service.WithEnvironment(ElsaStudioUrl, PublicUrl(studio));
        }

        ElsaStudio = studio;
        return Added<TProject>(studio, suffix);
    }

    private const string ElsaStudioBackendUrl = "Coworkee__Workflows__Elsa__Studio__BackendUrl";

    private const string ElsaStudioUrl = "Coworkee__Workflows__Elsa__StudioUrl";

    /// <summary>A host with Coworkee.Bff: signs in at the auth server as the next of its clients and forwards to the first api.</summary>
    private IResourceBuilder<ProjectResource> BffClient<TProject>(string suffix, string displayName, string caller)
        where TProject : IProjectMetadata, new()
    {
        var auth = Require(AuthServer, nameof(AddAuthServer), caller);
        var api = Require(_apis.FirstOrDefault(), nameof(AddApi), caller);
        var clientId = $"{Name}-{suffix}";
        var host = Exposed(Service<TProject>(suffix))
            .WithReference(api)
            .WaitFor(auth)
            .WithSetting(s => s.Coworkee.Bff.Authority, PublicUrl(auth))
            .WithSetting(s => s.Coworkee.Bff.ClientId, clientId)
            .WithSetting(s => s.Coworkee.Bff.ApiAddress, $"https+http://{api.Resource.Name}")
            .WithSettings(s => s.Coworkee.Bff.Scopes, ApiAudience);
        if (_redis is not null || Options.Redis is not null)
        {
            // the sign-in sessions survive a restart of the host and are shared between its instances
            WireRedis(host);
        }

        var redirect = PublicUrl(host);
        var index = _bffClients++;
        auth.WithSetting(s => s.Coworkee.Auth.Clients[index].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].DisplayName, displayName)
            .WithSettings(s => s.Coworkee.Auth.Clients[index].Scopes, ApiAudience)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].ClientUri, redirect)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].RedirectUris[0], ReferenceExpression.Create($"{redirect}/signin-oidc"))
            .WithSetting(s => s.Coworkee.Auth.Clients[index].PostLogoutRedirectUris[0], ReferenceExpression.Create($"{redirect}/signout-callback-oidc"));
        return host;
    }

    /// <summary>The auth server shows the design, the api hosts the settings that change it (and rewrites Keycloak's login theme from it).</summary>
    private void WithSignInDesign(IResourceBuilder<ProjectResource> service)
    {
        if (Options.SignIn.Layout is { } layout)
        {
            service.WithSetting(s => s.Coworkee.Auth.Design.Layout, layout);
        }

        if (Options.SignIn.MediaSide is { } side)
        {
            service.WithSetting(s => s.Coworkee.Auth.Design.MediaSide, side.ToString());
        }

        if (Options.SignIn.LoadingScreen is { } loading)
        {
            service.WithSetting(s => s.Coworkee.Auth.Design.LoadingScreen, loading.ToString());
        }

        if (Options.SignIn.MediaUrl is { } media && !media.StartsWith('/'))
        {
            service.WithSetting(s => s.Coworkee.Auth.Design.MediaUrl, media);
        }
    }

    private IResourceBuilder<ProjectResource> Service<TProject>(string suffix)
        where TProject : IProjectMetadata, new()
    {
        var service = Builder.AddProject<TProject>($"{Name}-{suffix}").WithHttpHealthCheck(Health);
        WithSetupToken(service);
        WireServices(service);
        WireGrafanaTelemetry(service);
        WireModules(service);
        return service;
    }

    private void WireModules(IResourceBuilder<ProjectResource> service, string? except = null)
    {
        var references = CoworkeeModules.References(service.Resource.GetProjectMetadata().ProjectPath);
        foreach (var (_, wire) in Modules.Where(m => references.Contains(m.Key) && !string.Equals(m.Key, except, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            wire(this, service);
        }
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

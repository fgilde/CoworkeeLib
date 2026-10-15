using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    internal const string KeycloakHeaderLimit = "QUARKUS_HTTP_LIMITS_MAX_HEADER_LIST_SIZE";

    private const string Provider = "keycloak";

    private readonly List<KeycloakClient> _keycloakClients = [];
    private IResourceBuilder<ParameterResource>? _keycloakPassword;
    private string? _realm;
    private string? _loginTheme;

    /// <summary>The folder mounted as Keycloak's themes folder (see <see cref="CoworkeeKeycloakOptions.LoginTheme"/>).</summary>
    public string KeycloakThemes => Path.Combine(Builder.AppHostDirectory, ".coworkee", "keycloak-themes");

    /// <summary>The users of the app's realm (<see cref="CoworkeeKeycloakOptions.Users"/>); <see cref="KeycloakPassword"/> gives each one's password.</summary>
    public IReadOnlyList<KeycloakUser> KeycloakUsers => Options.Keycloak?.Users ?? [];

    /// <summary>The clients of the app's realm: the auth server's first, then those of <see cref="AddKeycloakClient"/>.</summary>
    public IReadOnlyList<KeycloakClient> KeycloakClients => _keycloakClients;

    /// <summary>Issuer address of the app's realm, e.g. for the OpenID Connect settings of another resource.</summary>
    public ReferenceExpression KeycloakAuthority =>
        ReferenceExpression.Create($"{RequireKeycloak(nameof(KeycloakAuthority)).GetEndpoint("http")}/realms/{_realm}");

    /// <summary>
    /// Registers another confidential client of the app's realm (e.g. Grafana); the realm gets it with its secret and redirect addresses on every start.
    /// Without <paramref name="secret"/> a generated one is kept in the user secrets; without redirect addresses any local address is allowed.
    /// </summary>
    public KeycloakClient AddKeycloakClient(string clientId, IEnumerable<ReferenceExpression>? redirectUris = null, IResourceBuilder<ParameterResource>? secret = null)
    {
        RequireKeycloak(nameof(AddKeycloakClient));
        var client = new KeycloakClient(clientId, secret ?? GeneratedSecret($"{clientId}-secret", 32), [.. redirectUris ?? []]);
        _keycloakClients.Add(client);
        return client;
    }

    /// <summary>The Keycloak password of <paramref name="user"/>: its own, or the shared generated one.</summary>
    public ReferenceExpression KeycloakPassword(KeycloakUser user) => user.Password is { } password
        ? ReferenceExpression.Create($"{password}")
        : ReferenceExpression.Create($"{_keycloakPassword ?? throw new InvalidOperationException($"{user.Email} joined the Keycloak users after {nameof(AddAuthServer)} without a password.")}");

    private void AddKeycloak(IResourceBuilder<ProjectResource> auth, CoworkeeKeycloakOptions options)
    {
        _realm = options.Realm ?? Name;
        var authClient = new KeycloakClient($"{Name}-auth", GeneratedSecret($"{Name}-keycloak-client-secret", 32), []);
        _keycloakClients.Insert(0, authClient);
        if (options.Users.Any(u => u.Password is null))
        {
            _keycloakPassword = GeneratedSecret($"{Name}-keycloak-user-password", 16);
        }

        Keycloak = options.Resource ?? Builder.AddKeycloak("keycloak", options.Port);
        if (Persistent && options.Resource is null)
        {
            Keycloak.WithDataVolume();
        }

        if (options.LoginTheme && options.Resource is null && Builder.ExecutionContext.IsRunMode)
        {
            _loginTheme = KeycloakLoginTheme.NameFor(Name);
            Directory.CreateDirectory(KeycloakThemes);
            Keycloak.WithBindMount(KeycloakThemes, "/opt/keycloak/themes", isReadOnly: true);
        }

        // every local app's localhost cookies reach Keycloak too; over HTTP/2 it took 8 KB of headers and answered 431, now the 64 KB it takes over HTTP/1.1
        Configured(Keycloak, Options.KeycloakServer)
            .WithEnvironment(KeycloakHeaderLimit, "65536")
            .OnResourceReady((keycloak, ready, cancellationToken) => ReconcileRealmAsync(keycloak, ready.Services, options, cancellationToken));

        auth.WaitFor(Keycloak)
            .WithSetting(s => s.Coworkee.Auth.External.Mode, options.LoginMode.ToString())
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].DisplayName, options.DisplayName)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].Authority, KeycloakAuthority)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].ClientId, authClient.ClientId)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].ClientSecret, authClient.Secret)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].RequireHttpsMetadata, false)
            .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].TrustEmail, options.TrustEmail);
    }

    /// <summary>
    /// The api serves the settings, so it needs the mode and the provider names to define the sign-in methods setting. The auth server
    /// alone registers the providers (with their secrets), so nothing else of them goes here.
    /// </summary>
    private void AddKeycloakSignInSettings(IResourceBuilder<ProjectResource> api)
    {
        if (Options.Keycloak is { } options && Keycloak is not null)
        {
            api.WithSetting(s => s.Coworkee.Auth.External.Mode, options.LoginMode.ToString())
                .WithSetting(s => s.Coworkee.Auth.External.Providers[Provider].DisplayName, options.DisplayName);
        }

        // the api rewrites the login theme when the default theme or the sign-in design changes
        if (_loginTheme is { } theme)
        {
            api.WithSetting(s => s.Coworkee.Keycloak.ThemeDirectory, KeycloakThemes)
                .WithSetting(s => s.Coworkee.Keycloak.ThemeName, theme)
                .WithSetting(s => s.Coworkee.Keycloak.AppName, Title);
            if (Options.LogoUrl is { } logo)
            {
                api.WithSetting(s => s.Coworkee.Keycloak.LogoUrl, logo);
            }
        }
    }

    /// <summary>Writes the login theme from the app host's options unless it exists; from then on the api keeps it.</summary>
    internal async Task WriteLoginThemeAsync(CancellationToken cancellationToken)
    {
        if (_loginTheme is not { } theme || KeycloakLoginTheme.Exists(KeycloakThemes, theme))
        {
            return;
        }

        KeycloakLoginTheme.Write(KeycloakThemes, theme, new(Title, KeycloakLoginTheme.DefaultLight, KeycloakLoginTheme.DefaultDark)
        {
            LogoUrl = await OnWebAsync(Options.LogoUrl, cancellationToken),
            Design = new() { Layout = Options.SignIn.Layout, MediaSide = Options.SignIn.MediaSide, MediaUrl = await OnWebAsync(Options.SignIn.MediaUrl, cancellationToken) },
        });
    }

    // the theme is a nicety: a folder that cannot be written must not keep the realm, and with it the sign-in, from coming up
    internal async Task TryWriteLoginThemeAsync(ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await WriteLoginThemeAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(exception, "The Keycloak login theme {Theme} could not be written to {Folder}", _loginTheme, KeycloakThemes);
        }
    }

    private async Task<string?> OnWebAsync(string? url, CancellationToken cancellationToken) => url is not null && url.StartsWith('/') && Web is { } web
        ? await ReferenceExpression.Create($"{web.GetEndpoint("https")}{url}").GetValueAsync(cancellationToken)
        : url;

    private string Title => Options.DisplayName ?? Name;

    private IResourceBuilder<KeycloakResource> RequireKeycloak(string caller) =>
        Require(Keycloak, $"{nameof(CoworkeeAppOptions.UseKeycloak)} and {nameof(AddAuthServer)}", caller);

    private IResourceBuilder<ParameterResource> GeneratedSecret(string name, int length) =>
        Builder.AddParameter(name, new GenerateParameterDefault { MinLength = length, Special = false }, secret: true, persist: true);

    private async Task ReconcileRealmAsync(KeycloakResource keycloak, IServiceProvider services, CoworkeeKeycloakOptions options, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(keycloak);
        var clients = new List<KeycloakRealm.Client>();
        foreach (var client in _keycloakClients)
        {
            var uris = new List<string>();
            foreach (var uri in client.RedirectUris)
            {
                uris.Add((await uri.GetValueAsync(cancellationToken))!);
            }

            clients.Add(new(client.ClientId, (await client.Secret.Resource.GetValueAsync(cancellationToken))!, uris));
        }

        var users = new List<KeycloakUser>();
        foreach (var user in options.Users)
        {
            users.Add(user with { Password = await KeycloakPassword(user).GetValueAsync(cancellationToken) });
        }

        var admin = keycloak.AdminUserNameParameter is { } name ? await name.GetValueAsync(cancellationToken) : "admin";
        var adminPassword = await keycloak.AdminPasswordParameter.GetValueAsync(cancellationToken);
        // the container serves the ASP.NET development certificate, which a fresh machine may not trust yet
        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (request, _, _, errors) => errors == 0 || request.RequestUri!.IsLoopback })
        {
            BaseAddress = new Uri(keycloak.GetEndpoint("http").Url + "/"),
        };
        await TryWriteLoginThemeAsync(logger, cancellationToken);
        try
        {
            await KeycloakRealm.ReconcileAsync(http, admin!, adminPassword!, _realm!, new(Title, _loginTheme), clients, users, cancellationToken);
            logger.LogInformation("Realm {Realm} is up to date: clients {Clients}, users {Users}", _realm, string.Join(", ", clients.Select(c => c.ClientId)), string.Join(", ", users.Select(u => u.Email)));
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            logger.LogError(exception, "Realm {Realm} could not be brought up to date", _realm);
            throw;
        }
    }
}

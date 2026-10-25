using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private IResourceBuilder<ParameterResource>? _adminPassword;
    private bool _keycloakHostname;
    private bool _toolIssuer;

    private string AdminUser => Options.Administrator?.UserName ?? "admin";

    // a deployment never takes the development password of Options.Administrator
    private IResourceBuilder<ParameterResource> AdminPassword => _adminPassword ??= Options.Administrator is { } admin && !Publishing
        ? Builder.AddParameter($"{Name}-admin-password", admin.Password, secret: true)
        : GeneratedSecret($"{Name}-admin-password", 16);

    private void AddTools()
    {
        if (Options.GrafanaServer is { } grafana && Deploys(CoworkeeTools.Grafana))
        {
            AddGrafana(grafana);
        }

        if (Options.WebDataStudioServer is { } studio && Deploys(CoworkeeTools.WebDataStudio))
        {
            AddWebDataStudio(studio);
        }
    }

    private void AddToolsSignIn()
    {
        if (Grafana is { } grafana)
        {
            AddGrafanaSignIn(grafana);
        }

        if (WebDataStudio is { } studio)
        {
            AddWebDataStudioSignIn(studio);
        }
    }

    /// <summary>
    /// The realm as containers reach it. Keycloak then names itself by the browser's address (KC_HOSTNAME), whoever asks,
    /// so tokens fetched over the container network carry the same issuer as those of the auth server.
    /// </summary>
    private string ContainerKeycloakAuthority()
    {
        var keycloak = RequireKeycloak(nameof(ContainerKeycloakAuthority));
        var http = keycloak.GetEndpoint("http");
        if (!_keycloakHostname)
        {
            _keycloakHostname = true;
            keycloak.WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true");
            if (Publishing)
            {
                keycloak.WithEnvironment("KC_HOSTNAME", ToolUrl(keycloak));
            }
            else
            {
                keycloak.WithEnvironment(context => context.EnvironmentVariables["KC_HOSTNAME"] = http.Url);
            }
        }

        return $"http://{keycloak.Resource.Name}:{http.TargetPort}/realms/{_realm}";
    }

    /// <summary>
    /// A client of the auth server for a tool with a sign-in of its own (Grafana, WebDataStudio, AspireUI, Kibana's proxy): confidential, not in the launcher,
    /// administrators of the system organisation only. While running, the auth server then names itself by the browser's address and gives containers
    /// the token endpoint by theirs (<see cref="Coworkee.Contracts.Configuration.AuthServerOptions.DynamicBackchannel"/>).
    /// </summary>
    private (string ClientId, IResourceBuilder<ParameterResource> Secret) AddToolClient(string tool, string displayName, ReferenceExpression redirectUri)
    {
        var auth = AuthServer!;
        var clientId = $"{Name}-{tool}";
        var secret = GeneratedSecret($"{clientId}-secret", 32);
        var index = _bffClients++;
        auth.WithSetting(s => s.Coworkee.Auth.Clients[index].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].DisplayName, $"{Title} ({displayName})")
            .WithSetting(s => s.Coworkee.Auth.Clients[index].ClientSecret, secret)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].RedirectUris[0], redirectUri)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].ShowInLauncher, false)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].SystemAdministratorsOnly, true);
        if (!Publishing && !_toolIssuer)
        {
            _toolIssuer = true;
            auth.WithSetting(s => s.Coworkee.Auth.Issuer, PublicUrl(auth))
                .WithSetting(s => s.Coworkee.Auth.DynamicBackchannel, true);
        }

        return (clientId, secret);
    }

    /// <summary>An address of the auth server as the browser knows it; in a container's environment <see cref="PublicUrl"/> names the container network while running.</summary>
    private void WithAuthBrowserUrl<T>(IResourceBuilder<T> resource, string variable, string path)
        where T : IResourceWithEnvironment
    {
        var auth = AuthServer!;
        if (Publishing)
        {
            resource.WithEnvironment(variable, ReferenceExpression.Create($"{PublicUrl(auth)}{path}"));
        }
        else
        {
            var https = auth.GetEndpoint("https");
            resource.WithEnvironment(context => context.EnvironmentVariables[variable] = $"{https.Url}{path}");
        }
    }
}

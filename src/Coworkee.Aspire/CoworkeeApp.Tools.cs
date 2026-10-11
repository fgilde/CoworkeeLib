using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private IResourceBuilder<ParameterResource>? _adminPassword;
    private bool _keycloakHostname;

    private string AdminUser => Options.Administrator?.UserName ?? "admin";

    private IResourceBuilder<ParameterResource> AdminPassword => _adminPassword ??= Options.Administrator is { } admin
        ? Builder.AddParameter($"{Name}-admin-password", admin.Password, secret: true)
        : GeneratedSecret($"{Name}-admin-password", 16);

    // ponytail: development tools only, a deployment brings its own monitoring and database access
    private void AddTools()
    {
        if (!Builder.ExecutionContext.IsRunMode)
        {
            return;
        }

        if (Options.GrafanaServer is { } grafana)
        {
            AddGrafana(grafana);
        }

        if (Options.WebDataStudioServer is { } studio)
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
            keycloak.WithEnvironment(context =>
            {
                context.EnvironmentVariables["KC_HOSTNAME"] = http.Url;
                context.EnvironmentVariables["KC_HOSTNAME_BACKCHANNEL_DYNAMIC"] = "true";
            });
        }

        return $"http://{keycloak.Resource.Name}:{http.TargetPort}/realms/{_realm}";
    }

    private string KeycloakBrowserAuthority => $"{Keycloak!.GetEndpoint("http").Url}/realms/{_realm}";
}

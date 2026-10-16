using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private IResourceBuilder<ParameterResource>? _adminPassword;
    private bool _keycloakHostname;

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

    /// <summary>An address of the realm as the browser knows it, e.g. where a tool sends it to sign in.</summary>
    private void WithKeycloakBrowserUrl<T>(IResourceBuilder<T> resource, string variable, string path)
        where T : IResourceWithEnvironment
    {
        var keycloak = RequireKeycloak(nameof(WithKeycloakBrowserUrl));
        if (Publishing)
        {
            resource.WithEnvironment(variable, ReferenceExpression.Create($"{ToolUrl(keycloak)}/realms/{_realm}{path}"));
        }
        else
        {
            resource.WithEnvironment(context => context.EnvironmentVariables[variable] = $"{keycloak.GetEndpoint("http").Url}/realms/{_realm}{path}");
        }
    }
}

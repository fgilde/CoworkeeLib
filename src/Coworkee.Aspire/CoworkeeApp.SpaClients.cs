using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    private static readonly HashSet<string> ReservedScopes = new(StringComparer.OrdinalIgnoreCase) { "openid", "profile", "email", "roles", "offline_access" };

    private readonly List<ReferenceExpression> _spaOrigins = [];

    private readonly HashSet<string> _scopeAliases = new(StringComparer.Ordinal);

    /// <summary>
    /// A single-page app (a JavaScript resource or a container) that signs in at the auth server itself and calls the apis with its access token, without a BFF:
    /// a public client <paramref name="clientId"/> with PKCE whose redirect and post-logout address is the address of <paramref name="endpointName"/> (exactly,
    /// plus <paramref name="redirectPath"/>), the scope of the app's api plus <paramref name="scopes"/>, each of <paramref name="apiScopeAliases"/> as another
    /// name of the api's audience (not a standard scope, not a name taken already), and the origin allowed in the CORS policy of the auth server and of every api
    /// (<c>Coworkee:Cors:Origins</c>, also of apis added later). <paramref name="allowImpersonation"/>: "Log in as" works there. In a deployment the address is
    /// the parameter <c>{app}-{resource}-url</c>. Add it after the api.
    /// </summary>
    public IResourceBuilder<T> AddSpaClient<T>(
        string clientId,
        IResourceBuilder<T> resource,
        string endpointName = "http",
        IEnumerable<string>? scopes = null,
        IEnumerable<string>? apiScopeAliases = null,
        bool allowImpersonation = false,
        string redirectPath = "")
        where T : IResourceWithEndpoints
    {
        var auth = Require(AuthServer, nameof(AddAuthServer), nameof(AddSpaClient));
        Require(_apis.FirstOrDefault(), nameof(AddApi), nameof(AddSpaClient));
        var url = Publishing
            ? ReferenceExpression.Create($"{DeployParameter($"{Name}-{resource.Resource.Name}-url").WithDescription($"The public address of {resource.Resource.Name}, e.g. https://{resource.Resource.Name}.example.com")}")
            : ReferenceExpression.Create($"{resource.GetEndpoint(endpointName)}");
        var redirect = ReferenceExpression.Create($"{url}{redirectPath}");
        var aliases = apiScopeAliases?.ToList() ?? [];
        foreach (var alias in aliases)
        {
            if (ReservedScopes.Contains(alias) || alias == ApiAudience || !_scopeAliases.Add(alias))
            {
                throw new ArgumentException($"The API scope alias '{alias}' is a standard scope or taken already.", nameof(apiScopeAliases));
            }
        }

        var index = _bffClients++;
        auth.WithSetting(s => s.Coworkee.Auth.Clients[index].ClientId, clientId)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].DisplayName, $"{Title} ({resource.Resource.Name})")
            .WithSettings(s => s.Coworkee.Auth.Clients[index].Scopes, [ApiAudience, .. scopes ?? [], .. aliases])
            .WithSetting(s => s.Coworkee.Auth.Clients[index].ClientUri, url)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].RedirectUris[0], redirect)
            .WithSetting(s => s.Coworkee.Auth.Clients[index].PostLogoutRedirectUris[0], redirect);
        if (allowImpersonation)
        {
            auth.WithSetting(s => s.Coworkee.Auth.Clients[index].AllowImpersonation, true);
        }

        foreach (var alias in aliases)
        {
            auth.WithSetting(s => s.Coworkee.Auth.ApiScopes[alias], ApiAudience);
        }

        _spaOrigins.Add(url);
        return resource;
    }

    // read when the app host starts or publishes, so apis added after AddSpaClient get the origins too
    private void WithSpaOrigins(IResourceBuilder<ProjectResource> service) =>
        service.WithEnvironment(context =>
        {
            for (var i = 0; i < _spaOrigins.Count; i++)
            {
                context.EnvironmentVariables[$"{SettingPath.Of(s => s.Coworkee.Cors.Origins)}__{i}"] = _spaOrigins[i];
            }
        });
}

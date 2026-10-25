using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    public const string KibanaProxyImage = "oauth2-proxy/oauth2-proxy";
    public const string KibanaProxyTag = "v7.8.1";

    private IResourceBuilder<ElasticsearchResource>? _kibanaSearch;
    private IResourceBuilder<ParameterResource>? _kibanaPassword;

    /// <summary>Kibana of <see cref="CoworkeeAppOptions.UseKibana"/>, added with the Elasticsearch container; in a deployment unless <see cref="CoworkeeTools.Kibana"/> is switched off.</summary>
    public IResourceBuilder<ContainerResource>? Kibana { get; private set; }

    /// <summary>oauth2-proxy in front of <see cref="Kibana"/>: the browser signs in at the app's auth server, administrators of the system organisation only.</summary>
    public IResourceBuilder<ContainerResource>? KibanaSignIn { get; private set; }

    private void AddKibana(IResourceBuilder<ElasticsearchResource> search, Action<IResourceBuilder<ContainerResource>> configure)
    {
        _kibanaSearch = search;
        _kibanaPassword = GeneratedSecret($"{Name}-kibana-password", 24);
        var elasticsearch = search.GetEndpoint("http");
        // Kibana may not connect as the superuser elastic; the built-in user kibana_system gets its password as soon as Elasticsearch answers
        var setup = Builder.AddContainer("kibana-setup", "curlimages/curl", "8.11.1")
            .WithEntrypoint("sh")
            .WithArgs("-c", """until curl -sf -u "elastic:$ELASTIC_PASSWORD" -H 'Content-Type: application/json' -d "{\"password\":\"$KIBANA_PASSWORD\"}" "$ELASTICSEARCH/_security/user/kibana_system/_password"; do sleep 5; done""")
            .WithEnvironment("ELASTICSEARCH", elasticsearch)
            .WithEnvironment("ELASTIC_PASSWORD", search.Resource.PasswordParameter)
            .WithEnvironment("KIBANA_PASSWORD", _kibanaPassword)
            .WaitFor(search);

        // the version of Kibana has to match the one of Elasticsearch
        var image = search.Resource.Annotations.OfType<ContainerImageAnnotation>().Last();
        Kibana = Builder.AddContainer("kibana-server", "library/kibana", image.Tag!)
            .WithImageRegistry(image.Registry)
            .WithHttpEndpoint(targetPort: 5601, name: "http")
            // only oauth2-proxy is the way in: no link in the dashboard, and a deployment publishes no port of Kibana itself
            .WithUrls(context => context.Urls.Clear())
            .WithEnvironment("ELASTICSEARCH_HOSTS", elasticsearch)
            .WithEnvironment("ELASTICSEARCH_USERNAME", "kibana_system")
            .WithEnvironment("ELASTICSEARCH_PASSWORD", _kibanaPassword)
            .WaitForCompletion(setup);
        configure(Kibana);
        AddKibanaSignIn();
    }

    /// <summary>
    /// With the auth server in place (added before or after the search): Kibana lets everyone in as the user elastic (its anonymous provider, part of the free license,
    /// unlike its own single sign-on) and only oauth2-proxy reaches it, after the auth server let an administrator of the system organisation in.
    /// Without the auth server Kibana keeps its own login (user elastic, the password of Elasticsearch).
    /// </summary>
    private void AddKibanaSignIn()
    {
        if (Kibana is not { } kibana || AuthServer is not { } auth || KibanaSignIn is not null)
        {
            return;
        }

        const string provider = "--xpack.security.authc.providers.anonymous.anonymous1";
        // ponytail: the password of elastic is an argument of the Kibana process; a kibana.yml mounted from a secret if the host's process list must not show it
        kibana.WithArgs("/usr/local/bin/kibana-docker", $"{provider}.order=0", $"{provider}.credentials.username=elastic",
            ReferenceExpression.Create($"{provider}.credentials.password={_kibanaSearch!.Resource.PasswordParameter}"));

        var proxy = Builder.AddContainer(CoworkeeTools.Kibana, KibanaProxyImage, KibanaProxyTag)
            .WithImageRegistry("quay.io")
            .WithHttpEndpoint(targetPort: 4180, name: "http")
            .WithUrlForEndpoint("http", u => u.DisplayText = "Kibana")
            .WaitFor(kibana)
            .WaitFor(auth);
        var url = ToolUrl(proxy);
        var (clientId, secret) = AddToolClient("kibana", "Kibana", ReferenceExpression.Create($"{url}/oauth2/callback"));
        // the proxy redeems the code over the container network while running, at the public address deployed
        var backchannel = PublicUrl(auth);
        proxy.WithEnvironment("OAUTH2_PROXY_PROVIDER", "oidc")
            .WithEnvironment("OAUTH2_PROXY_PROVIDER_DISPLAY_NAME", Title)
            .WithEnvironment("OAUTH2_PROXY_CLIENT_ID", clientId)
            .WithEnvironment("OAUTH2_PROXY_CLIENT_SECRET", secret)
            .WithEnvironment("OAUTH2_PROXY_CODE_CHALLENGE_METHOD", "S256")
            .WithEnvironment("OAUTH2_PROXY_SCOPE", "openid profile email")
            .WithEnvironment("OAUTH2_PROXY_SKIP_OIDC_DISCOVERY", "true")
            .WithEnvironment("OAUTH2_PROXY_REDEEM_URL", ReferenceExpression.Create($"{backchannel}/connect/token"))
            .WithEnvironment("OAUTH2_PROXY_OIDC_JWKS_URL", ReferenceExpression.Create($"{backchannel}/.well-known/jwks"))
            // the auth server issues no email_verified; it decides who gets in (SystemAdministratorsOnly)
            .WithEnvironment("OAUTH2_PROXY_INSECURE_OIDC_ALLOW_UNVERIFIED_EMAIL", "true")
            .WithEnvironment("OAUTH2_PROXY_EMAIL_DOMAINS", "*")
            .WithEnvironment("OAUTH2_PROXY_SKIP_PROVIDER_BUTTON", "true")
            .WithEnvironment("OAUTH2_PROXY_UPSTREAMS", kibana.GetEndpoint("http"))
            .WithEnvironment("OAUTH2_PROXY_HTTP_ADDRESS", "0.0.0.0:4180")
            .WithEnvironment("OAUTH2_PROXY_COOKIE_SECRET", GeneratedSecret($"{clientId}-cookie-secret", 32))
            // browsers send cookies to every port of localhost
            .WithEnvironment("OAUTH2_PROXY_COOKIE_NAME", $"_{Name}_kibana")
            .WithEnvironment("OAUTH2_PROXY_COOKIE_SECURE", Publishing ? "true" : "false")
            .WithEnvironment("OAUTH2_PROXY_REVERSE_PROXY", Publishing ? "true" : "false");
        // the browser's addresses: inside the container the endpoints would resolve to the container network (aspire.dev.internal, kibana.dev.internal);
        // the issuer is the browser's address of the auth server as well (Coworkee:Auth:Issuer), with the slash OpenIddict puts into iss
        WithAuthBrowserUrl(proxy, "OAUTH2_PROXY_OIDC_ISSUER_URL", "/");
        WithAuthBrowserUrl(proxy, "OAUTH2_PROXY_LOGIN_URL", "/connect/authorize");
        WithBrowserUrl(proxy, "OAUTH2_PROXY_REDIRECT_URL", "/oauth2/callback");

        KibanaSignIn = proxy;
    }
}

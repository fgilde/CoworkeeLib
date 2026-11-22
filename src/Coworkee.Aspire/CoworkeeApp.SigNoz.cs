using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.SigNoz;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    /// <summary>Setting with the OTLP address of SigNoz's collector; the service defaults send metrics, logs and traces there as well (next to Grafana and the Aspire dashboard).</summary>
    public const string SigNozOtlpEndpoint = "SIGNOZ_OTLP_ENDPOINT";

    /// <summary>The protocol of <see cref="SigNozOtlpEndpoint"/>: gRPC while running, <c>http/protobuf</c> in a deployment.</summary>
    public const string SigNozOtlpProtocol = "SIGNOZ_OTLP_PROTOCOL";

    /// <summary>SigNoz of <see cref="CoworkeeAppOptions.UseSigNoz"/>; in a deployment unless <see cref="CoworkeeTools.SigNoz"/> is switched off.</summary>
    public IResourceBuilder<SigNozResource>? SigNoz { get; private set; }

    private void AddSigNoz(Action<IResourceBuilder<SigNozResource>> configure)
    {
        // no volumes while developing, like Grafana: the admin, dashboards and alert rules are provisioned on every start anyway
        SigNoz = Builder.AddSigNoz(CoworkeeTools.SigNoz, ObservabilityRoot);
        if (Publishing)
        {
            // SigNoz takes the administrator of the deployment (coworkee deploy generates a password SigNoz accepts)
            SigNoz.WithAdminUser(AdminEmail, ReferenceExpression.Create($"{AdminPassword}"), Title)
                .WithDataVolume(DataVolume(CoworkeeTools.SigNoz))
                .WithClickHouse(c => c.WithDataVolume(DataVolume("signoz-clickhouse")));
            // the browser reaches SigNoz itself unless the auth server's sign-in proxy stands in front (added with the auth server, after this)
            Builder.Eventing.Subscribe<BeforeStartEvent>((_, _) =>
            {
                if (SigNoz.Resource.SignIn is null)
                {
                    var url = ToolUrl(SigNoz, SigNozResource.HttpEndpointName);
                    SigNoz.WithEnvironment("SIGNOZ_GLOBAL_EXTERNAL__URL", url).WithEnvironment("SIGNOZ_ALERTMANAGER_SIGNOZ_EXTERNAL__URL", url);
                }

                return Task.CompletedTask;
            });
        }
        else if (Options.Administrator is { } admin && SigNozBuilderExtensions.IsValidAdminPassword(admin.Password))
        {
            // SigNoz wants an email address with a domain; a user name gets one
            SigNoz.WithAdminUser(admin.UserName.Contains('@', StringComparison.Ordinal) ? admin.UserName : $"{admin.UserName}@{Name.ToLowerInvariant()}.local", AdminPassword, Title);
        }

        // otherwise SigNoz keeps its own admin (admin@signoz.local, the generated parameter signoz-password): the app's password does not meet its rule
        configure(SigNoz);
    }

    /// <summary>
    /// SigNoz's own single sign-on is part of its licensed edition: oauth2-proxy (<c>signoz-signin</c>) signs the browser in at the auth server, administrators
    /// of the system organisation only, and SigNoz behind it acts as its admin for each of them. Like Kibana.
    /// </summary>
    private void AddSigNozSignIn(IResourceBuilder<SigNozResource> signoz)
    {
        var auth = AuthServer!;
        // the addresses are known only when the app host runs: set below, after the placeholders WithOidcLogin insists on
        signoz.WithOidcLogin(new SigNozOidcOptions
        {
            Name = Title,
            IssuerUrl = "https://auth",
            ClientId = $"{Name}-signoz",
            ClientSecret = ReferenceExpression.Create($"placeholder"),
            LoginUrl = "https://auth/connect/authorize",
            RedeemUrl = "https://auth/connect/token",
            JwksUrl = "https://auth/.well-known/jwks",
        });
        var proxy = Builder.CreateResourceBuilder(signoz.Resource.SignIn!);
        var url = ToolUrl(proxy);
        var (clientId, secret) = AddToolClient(CoworkeeTools.SigNoz, "SigNoz", ReferenceExpression.Create($"{url}/oauth2/callback"));
        var backchannel = PublicUrl(auth);
        proxy.WithEnvironment("OAUTH2_PROXY_CLIENT_ID", clientId)
            .WithEnvironment("OAUTH2_PROXY_CLIENT_SECRET", secret)
            .WithEnvironment("OAUTH2_PROXY_REDEEM_URL", ReferenceExpression.Create($"{backchannel}/connect/token"))
            .WithEnvironment("OAUTH2_PROXY_OIDC_JWKS_URL", ReferenceExpression.Create($"{backchannel}/.well-known/jwks"))
            // the auth server issues no email_verified; it decides who gets in (SystemAdministratorsOnly)
            .WithEnvironment("OAUTH2_PROXY_INSECURE_OIDC_ALLOW_UNVERIFIED_EMAIL", "true")
            .WithEnvironment("OAUTH2_PROXY_COOKIE_NAME", $"_{Name}_signoz")
            .WaitFor(auth);
        // the browser's addresses; the issuer is the browser's address of the auth server, with the slash OpenIddict puts into iss
        WithAuthBrowserUrl(proxy, "OAUTH2_PROXY_OIDC_ISSUER_URL", "/");
        WithAuthBrowserUrl(proxy, "OAUTH2_PROXY_LOGIN_URL", "/connect/authorize");
        if (Publishing)
        {
            // links of SigNoz and the redirect after signing in: the public address of the proxy (WithOidcLogin points them at its container address)
            proxy.WithEnvironment("OAUTH2_PROXY_REDIRECT_URL", ReferenceExpression.Create($"{url}/oauth2/callback"));
            signoz.WithEnvironment("SIGNOZ_GLOBAL_EXTERNAL__URL", url).WithEnvironment("SIGNOZ_ALERTMANAGER_SIGNOZ_EXTERNAL__URL", url);
        }
    }

    private void WireSigNozTelemetry(IResourceBuilder<ProjectResource> service)
    {
        if (SigNoz is { } signoz)
        {
            service.WithSigNozTelemetry(signoz, SigNozOtlpEndpoint, SigNozOtlpProtocol);
            if (Publishing)
            {
                // the dashboards rate over a minute; one export a minute would leave them mostly empty
                service.WithEnvironment("OTEL_METRIC_EXPORT_INTERVAL", "10000");
            }
        }
    }
}

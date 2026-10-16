using Aspire.Hosting.ApplicationModel;
using Coworkee.Contracts.Configuration;
using Nextended.Aspire.Hosting.Grafana;
using Nextended.Aspire.Hosting.Observability;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    /// <summary>Setting with the OTLP address of the Grafana stack; the service defaults send metrics, logs and traces there as well as to the Aspire dashboard.</summary>
    public const string GrafanaOtlpEndpoint = "GRAFANA_OTLP_ENDPOINT";

    private const string DatabaseDatasource = "App database";

    /// <summary>Grafana of <see cref="CoworkeeAppOptions.UseGrafana"/>; in a deployment unless <see cref="CoworkeeTools.Grafana"/> is switched off.</summary>
    public IResourceBuilder<GrafanaResource>? Grafana { get; private set; }

    private string ObservabilityRoot => Path.Combine(Builder.AppHostDirectory, "observability");

    private void AddGrafana(Action<IResourceBuilder<GrafanaResource>> configure)
    {
        var dashboards = Path.Combine(ObservabilityRoot, ".generated", "coworkee-dashboards");
        // before the stack's own hook, which bakes the dashboards into the published image
        Builder.Eventing.Subscribe<BeforeStartEvent>((_, _) => WriteDashboardsAsync(dashboards));
        // no data volume while developing: the admin password applies on every start, datasources and dashboards are provisioned anyway
        Grafana = Builder.AddGrafana("grafana", ObservabilityRoot)
            .WithEnvironment("GF_SECURITY_ADMIN_USER", AdminEmail)
            .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("GF_SECURITY_ADMIN_EMAIL", Publishing || AdminUser.Contains('@', StringComparison.Ordinal) ? AdminEmail : ReferenceExpression.Create($"{AdminUser}@localhost"))
            .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "false")
            // ponytail: root, because Docker Desktop's file sharing answered Grafana's own user with I/O errors on the provisioning folder; published, the files are baked into the image
            .WithContainerRuntimeArgs("--user", "0")
            .WithPrometheus(configure: p => p.WithRetention("7d"))
            .WithLoki()
            .WithTempo()
            // the services send to the Aspire dashboard themselves
            .WithOtelCollector(aspireDashboardOtlpEndpoint: "")
            .WithDashboards(dashboards, Options.DisplayName ?? Name);
        AddDatabaseDatasource(Grafana);
        if (Publishing)
        {
            PublishGrafana(Grafana);
        }

        configure(Grafana);
    }

    // users, metrics, logs and traces outlive a redeployment; the browser reaches Grafana behind the proxy
    private void PublishGrafana(IResourceBuilder<GrafanaResource> grafana)
    {
        grafana.WithDataVolume(DataVolume("grafana"))
            .WithEnvironment("GF_SERVER_ROOT_URL", ToolUrl(grafana, GrafanaResource.HttpEndpointName));
        foreach (var prometheus in Builder.Resources.OfType<PrometheusResource>())
        {
            Builder.CreateResourceBuilder(prometheus).WithDataVolume(DataVolume("prometheus"));
        }

        foreach (var loki in Builder.Resources.OfType<LokiResource>())
        {
            Builder.CreateResourceBuilder(loki).WithDataVolume(DataVolume("loki"));
        }

        foreach (var tempo in Builder.Resources.OfType<TempoResource>())
        {
            Builder.CreateResourceBuilder(tempo).WithDataVolume(DataVolume("tempo"));
        }
    }

    private void AddDatabaseDatasource(IResourceBuilder<GrafanaResource> grafana)
    {
        if (Options.Database is not null)
        {
            return;
        }

        switch (Options.DatabaseProvider)
        {
            case DatabaseProviders.Postgres:
                grafana.WithPostgresDatasource(Server, DatabaseDatasource, Database.Resource.DatabaseName).WithPostgresExporter(Server);
                break;
            case DatabaseProviders.SqlServer:
                var datasource = new GrafanaDatasource { Name = DatabaseDatasource, Uid = "app-database", Type = "mssql", Url = $"{SqlServer.Resource.Name}:1433", User = "sa" };
                datasource.JsonData["database"] = SqlServerDatabase.Resource.DatabaseName;
                datasource.JsonData["encrypt"] = "false";
                datasource.SecureJsonData["password"] = "${GF_DS_APP_DATABASE_PASSWORD}";
                grafana.WithDatasource(datasource).WithEnvironment("GF_DS_APP_DATABASE_PASSWORD", SqlServer.Resource.PasswordParameter);
                break;
        }
    }

    private void AddGrafanaSignIn(IResourceBuilder<GrafanaResource> grafana)
    {
        var options = Options.Keycloak!;
        var client = AddKeycloakClient($"{Name}-grafana", [ReferenceExpression.Create($"{ToolUrl(grafana, GrafanaResource.HttpEndpointName)}/login/generic_oauth")]);
        // a deployed administrator is a Keycloak user with the role Administrator
        var administrator = Publishing ? "" : $"email == '{AdminUser.Replace("'", "\\'", StringComparison.Ordinal)}' && 'GrafanaAdmin' || ";
        grafana.WithOAuthLogin(GrafanaOAuthOptions.Keycloak(ContainerKeycloakAuthority(), client.ClientId, ReferenceExpression.Create($"{client.Secret}"),
            roleAttributePath: $"{administrator}{GrafanaOAuthOptions.Roles("roles", grafanaAdmins: ["Administrator"])} || 'Viewer'", name: options.DisplayName));
        if (Publishing)
        {
            // WithOAuthLogin points the root at the container address
            grafana.WithEnvironment("GF_SERVER_ROOT_URL", ToolUrl(grafana, GrafanaResource.HttpEndpointName));
        }

        // the browser signs in at the address it knows Keycloak by; token and user info stay on the container network
        WithKeycloakBrowserUrl(grafana, "GF_AUTH_GENERIC_OAUTH_AUTH_URL", "/protocol/openid-connect/auth");
        grafana
            // WithOAuthLogin allows the provider only; the local administrator signs in as well, Keycloak users with the same email are the same Grafana user
            .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "false")
            .WithEnvironment("GF_AUTH_BASIC_ENABLED", "true")
            .WithEnvironment("GF_SECURITY_DISABLE_INITIAL_ADMIN_CREATION", "false")
            .WithEnvironment("GF_AUTH_OAUTH_ALLOW_INSECURE_EMAIL_LOOKUP", "true");
    }

    /// <summary>The protocol of <see cref="GrafanaOtlpEndpoint"/>: gRPC while running, <c>http/protobuf</c> in a deployment, where the collector takes OTLP over HTTP only.</summary>
    public const string GrafanaOtlpProtocol = "GRAFANA_OTLP_PROTOCOL";

    private void WireGrafanaTelemetry(IResourceBuilder<ProjectResource> service)
    {
        if (Grafana is null || Builder.Resources.OfType<OtelCollectorResource>().FirstOrDefault() is not { } collector)
        {
            return;
        }

        if (Publishing)
        {
            var http = collector.OtlpHttpEndpoint;
            service.WithEnvironment(GrafanaOtlpEndpoint, ReferenceExpression.Create($"http://{http.Property(EndpointProperty.Host)}:{http.Property(EndpointProperty.TargetPort)}"))
                .WithEnvironment(GrafanaOtlpProtocol, "http/protobuf");
        }
        else
        {
            service.WithEnvironment(GrafanaOtlpEndpoint, collector.OtlpGrpcEndpoint);
        }
    }

    private async Task WriteDashboardsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var assembly = typeof(CoworkeeApp).Assembly;
        var database = Options.Database is null ? Options.DatabaseProvider.ToLowerInvariant() : null;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Dashboards.", StringComparison.Ordinal)))
        {
            var file = name["Dashboards.".Length..];
            var provider = Path.GetFileNameWithoutExtension(file).Split('.').ElementAtOrDefault(1);
            var target = Path.Combine(folder, file);
            if (provider is not null && provider != database)
            {
                File.Delete(target);
                continue;
            }

            await using var source = assembly.GetManifestResourceStream(name)!;
            await using var stream = File.Create(target);
            await source.CopyToAsync(stream);
        }
    }
}

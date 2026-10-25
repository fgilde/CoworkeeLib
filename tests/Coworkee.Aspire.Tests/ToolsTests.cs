using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.AspireUI;
using Nextended.Aspire.Hosting.Grafana;
using Nextended.Aspire.Hosting.WebDataStudio;

namespace Coworkee.Aspire.Tests;

public sealed class ToolsTests
{
    private static readonly CoworkeeLogin Admin = new("admin@demo.test", "Admin-Password1");

    [Fact]
    public void Grafana_and_webdatastudio_are_off_unless_asked()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseGrafana().UseWebDataStudio().WithoutGrafana().WithoutWebDataStudio());

        app.AddProjects();

        builder.Resources.OfType<GrafanaResource>().ShouldBeEmpty();
        builder.Resources.OfType<WebDataStudioResource>().ShouldBeEmpty();
        app.Grafana.ShouldBeNull();
    }

    [Fact]
    public async Task Grafana_receives_the_services_telemetry_and_signs_in_the_administrator()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => { o.Administrator = Admin; o.UseGrafana(g => g.WithEnvironment("MINE", "1")); });

        app.AddProjects();
        Allocated(app.AuthServer!.Resource);

        builder.Resources.Select(r => r.Name).ShouldContain("prometheus");
        builder.Resources.Select(r => r.Name).ShouldContain("loki");
        builder.Resources.Select(r => r.Name).ShouldContain("tempo");
        builder.Resources.Select(r => r.Name).ShouldContain("postgres-exporter");
        foreach (var service in new IResourceWithEnvironment[] { app.Migrations!.Resource, app.Apis[0].Resource, app.Web!.Resource })
        {
            (await Environment(service))[CoworkeeApp.GrafanaOtlpEndpoint].ShouldBe("{otel-collector.bindings.otlp-grpc.url}");
        }

        var grafana = await Environment(Allocated(app.Grafana!.Resource));
        grafana["GF_SECURITY_ADMIN_USER"].ShouldBe(Admin.UserName);
        grafana["GF_SECURITY_ADMIN_PASSWORD"].ShouldBe("{demo-admin-password.value}");
        grafana["GF_DS_APP_DATABASE_PASSWORD"].ShouldBe("{postgres-password.value}");
        grafana["MINE"].ShouldBe("1");
        app.Grafana.Resource.Annotations.OfType<ContainerMountAnnotation>().ShouldContain(m => m.Target == "/var/lib/grafana/dashboards");
    }

    [Fact]
    public async Task Webdatastudio_opens_the_app_database_for_the_administrator()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => { o.Administrator = Admin; o.UseWebDataStudio(); });

        app.AddProjects();

        var studio = await Environment(app.WebDataStudio!.Resource);
        studio["WDS_USER"].ShouldBe(Admin.UserName);
        studio["WDS_PASSWORD"].ShouldBe("{demo-admin-password.value}");
        studio["WDS_CONN_DEMO"].ShouldEndWith("Database=demo");
        studio["WDS_TITLE"].ShouldBe("demo data");
        app.WebDataStudio.Resource.Annotations.OfType<ContainerMountAnnotation>().Single(m => m.Target == "/data").Source.ShouldBe("demo-webdatastudio-data");
    }

    [Fact]
    public async Task Both_sign_in_at_the_auth_server_and_keep_their_own_login()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => { o.Administrator = Admin; o.UseGrafana().UseWebDataStudio(); });

        app.AddProjects();
        Allocated(app.AuthServer!.Resource);

        Allocated(app.Grafana!.Resource);
        var grafana = await Environment(app.Grafana.Resource);
        // the browser goes to the auth server's address on the host, Grafana itself over the container network
        grafana["GF_AUTH_GENERIC_OAUTH_AUTH_URL"].ShouldBe("https://localhost:8180/connect/authorize");
        grafana["GF_AUTH_GENERIC_OAUTH_TOKEN_URL"].ShouldBe("{demo-auth.bindings.https.url}/connect/token");
        grafana["GF_AUTH_GENERIC_OAUTH_API_URL"].ShouldBe("{demo-auth.bindings.https.url}/connect/userinfo");
        grafana["GF_AUTH_GENERIC_OAUTH_CLIENT_SECRET"].ShouldBe("{demo-grafana-secret.value}");
        grafana["GF_AUTH_GENERIC_OAUTH_ROLE_ATTRIBUTE_PATH"].ShouldBe("'GrafanaAdmin'");
        grafana["GF_AUTH_DISABLE_LOGIN_FORM"].ShouldBe("false");
        grafana["GF_SECURITY_DISABLE_INITIAL_ADMIN_CREATION"].ShouldBe("false");

        var studio = await Environment(Allocated(app.WebDataStudio!.Resource));
        studio["WDS_OIDC_AUTHORITY"].ShouldBe("{demo-auth.bindings.https.url}");
        studio["WDS_OIDC_CLIENT_SECRET"].ShouldBe("{demo-webdatastudio-secret.value}");
        studio["WDS_OIDC_ADMINS"].ShouldBe("admin");
        studio["WDS_USER"].ShouldBe(Admin.UserName);

        // administrators of the system organisation only; the auth server names itself by the browser's address and hands containers their own
        var auth = await Environment(app.AuthServer.Resource);
        auth["Coworkee__Auth__Issuer"].ShouldBe("{demo-auth.bindings.https.url}");
        auth["Coworkee__Auth__DynamicBackchannel"].ShouldBe("true");
        foreach (var tool in new[] { "demo-grafana", "demo-webdatastudio" })
        {
            var client = auth.Single(e => e.Value == tool).Key.Replace("__ClientId", "", StringComparison.Ordinal);
            auth[$"{client}__SystemAdministratorsOnly"].ShouldBe("true");
            auth[$"{client}__ShowInLauncher"].ShouldBe("false");
        }

        auth[auth.Single(e => e.Value == "demo-webdatastudio").Key.Replace("__ClientId", "__RedirectUris__0", StringComparison.Ordinal)]
            .ShouldBe("{webdatastudio.bindings.http.url}/signin-oidc");
    }

    [Fact]
    public async Task Sql_server_is_the_database_of_both()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseSqlServer().UseGrafana().UseWebDataStudio());

        app.AddProjects();
        Allocated(app.AuthServer!.Resource);

        builder.Resources.Select(r => r.Name).ShouldNotContain("postgres-exporter");
        (await Environment(Allocated(app.Grafana!.Resource)))["GF_DS_APP_DATABASE_PASSWORD"].ShouldBe("{sqlserver-password.value}");
        var studio = await Environment(app.WebDataStudio!.Resource);
        studio["WDS_CONN_DEMO"].ShouldContain("demo");
        studio["WDS_CONN_DEMO_ENGINE"].ShouldBe("sqlserver");
        studio["WDS_USER"].ShouldBe("admin");
        studio["WDS_PASSWORD"].ShouldBe("{demo-admin-password.value}");
    }

    [Fact]
    public async Task Aspireui_signs_in_at_the_auth_server_as_its_admin()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseKeycloak());
        app.AddProjects();

        var aspireUi = builder.AddAspireUI().WithCoworkeeSignOn(app);

        app.KeycloakClients.Select(c => c.ClientId).ShouldBe(["demo-auth"]);
        var env = await Environment(Allocated(aspireUi.Resource));
        env["ASPIREUI_SET_OidcAuthority"].ShouldBe("{demo-auth.bindings.https.url}");
        env["ASPIREUI_SET_OidcClientId"].ShouldBe("demo-aspireui");
        env["ASPIREUI_SET_OidcClientSecret"].ShouldBe("{demo-aspireui-secret.value}");
        env["ASPIREUI_SET_OidcGroupsClaim"].ShouldBe("role");
        env["ASPIREUI_SET_OidcAdminGroup"].ShouldBe("admin");
        env["ASPIREUI_SET_FORCE"].ShouldBe("true");
        var auth = await Environment(Allocated(app.AuthServer!.Resource));
        auth[auth.Single(e => e.Value == "demo-aspireui").Key.Replace("__ClientId", "__RedirectUris__0", StringComparison.Ordinal)]
            .ShouldBe("{aspireui.bindings.http.url}/api/auth/sso/callback");
    }

    [Fact]
    public async Task Sqlite_is_mounted_into_the_studio_and_left_out_of_grafana()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseSqlite().UseGrafana().UseWebDataStudio());

        app.AddProjects();
        Allocated(app.AuthServer!.Resource);

        (await Environment(Allocated(app.Grafana!.Resource))).Keys.ShouldNotContain("GF_DS_APP_DATABASE_PASSWORD");
        app.WebDataStudio!.Resource.Annotations.OfType<ContainerMountAnnotation>().ShouldContain(m => m.Target == "/data/app");
        (await Environment(app.WebDataStudio.Resource))["WDS_CONN_DEMO"].ShouldBe("Data Source=/data/app/demo.db");
    }

    [Fact]
    public void The_sqlite_database_of_the_studio_uses_a_rollback_journal()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(folder, "demo.db");
        Sql(path, "PRAGMA journal_mode=WAL; CREATE TABLE x(a)");
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCoworkeeApp("demo", o => o.UseSqlite(path).UseWebDataStudio()).AddProjects();

        Sql(path, "PRAGMA journal_mode").ShouldBe("delete");
        Directory.Delete(folder, true);
    }

    private static object? Sql(string path, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public void Kibana_comes_only_when_asked_and_with_the_search_container()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseKibana().WithoutKibana());
        app.AddProjects();
        app.Kibana.ShouldBeNull();

        builder = DistributedApplication.CreateBuilder();
        app = builder.AddCoworkeeApp("demo", o => o.UseKibana().Without(Coworkee.Aspire.Modules.CoworkeeModules.Search));
        app.AddProjects();
        app.Kibana.ShouldBeNull();
        builder.Resources.Select(r => r.Name).ShouldNotContain("kibana-setup");
    }

    [Fact]
    public async Task Kibana_lets_in_whom_the_auth_server_lets_through_its_proxy()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseKibana(k => k.WithEnvironment("MINE", "1")));

        app.AddProjects();

        var search = builder.Resources.OfType<ElasticsearchResource>().Single();
        var kibana = app.Kibana!.Resource;
        kibana.Annotations.OfType<ContainerImageAnnotation>().Single().Tag.ShouldBe(search.Annotations.OfType<ContainerImageAnnotation>().Last().Tag);
        var env = await Environment(kibana);
        env["ELASTICSEARCH_USERNAME"].ShouldBe("kibana_system");
        env["ELASTICSEARCH_PASSWORD"].ShouldBe("{demo-kibana-password.value}");
        env["MINE"].ShouldBe("1");
        var args = await kibana.GetArgumentValuesAsync(DistributedApplicationOperation.Publish);
        args.ShouldContain("--xpack.security.authc.providers.anonymous.anonymous1.credentials.username=elastic");
        (await Environment(builder.Resources.OfType<ContainerResource>().Single(r => r.Name == "kibana-setup")))["KIBANA_PASSWORD"].ShouldBe("{demo-kibana-password.value}");

        Allocated(app.AuthServer!.Resource);
        var proxy = await Environment(Allocated(app.KibanaSignIn!.Resource));
        proxy["OAUTH2_PROXY_CLIENT_ID"].ShouldBe("demo-kibana");
        proxy["OAUTH2_PROXY_CLIENT_SECRET"].ShouldBe("{demo-kibana-secret.value}");
        proxy["OAUTH2_PROXY_UPSTREAMS"].ShouldBe("{kibana-server.bindings.http.url}");
        // the browser follows both: host addresses, not those of the container network
        proxy["OAUTH2_PROXY_REDIRECT_URL"].ShouldBe("http://localhost:8180/oauth2/callback");
        proxy["OAUTH2_PROXY_LOGIN_URL"].ShouldBe("https://localhost:8180/connect/authorize");
        kibana.Annotations.OfType<ResourceUrlsCallbackAnnotation>().ShouldNotBeEmpty();

        var auth = await Environment(Allocated(app.AuthServer!.Resource));
        var client = auth.Single(e => e.Value == "demo-kibana").Key.Replace("__ClientId", "", StringComparison.Ordinal);
        auth[$"{client}__SystemAdministratorsOnly"].ShouldBe("true");
        auth[$"{client}__ClientSecret"].ShouldBe("{demo-kibana-secret.value}");
        auth[$"{client}__RedirectUris__0"].ShouldBe("{kibana.bindings.http.url}/oauth2/callback");
    }

    [Fact]
    public async Task Open_webui_chats_with_ollama_and_signs_in_at_the_auth_server()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.Administrator = Admin);
        app.AddProjects();
        var ollama = builder.AddContainer("ollama", "ollama/ollama").WithHttpEndpoint(targetPort: 11434, name: "http");

        var ui = app.AddOpenWebUI(ollama);

        Allocated(app.AuthServer!.Resource);
        Allocated(ollama.Resource);
        var env = await Environment(Allocated(ui.Resource));
        env["OLLAMA_BASE_URL"].ShouldBe("{ollama.bindings.http.url}");
        env["WEBUI_ADMIN_EMAIL"].ShouldBe(Admin.UserName);
        env["ENABLE_SIGNUP"].ShouldBe("false");
        env["ENABLE_PERSISTENT_CONFIG"].ShouldBe("false");
        env["OAUTH_CLIENT_ID"].ShouldBe("demo-openwebui");
        env["OAUTH_CODE_CHALLENGE_METHOD"].ShouldBe("S256");
        env["OAUTH_CLIENT_SECRET"].ShouldBe("{demo-openwebui-secret.value}");
        // discovery over the container network, the browser comes back to the host address
        env["OPENID_PROVIDER_URL"].ShouldBe("{demo-auth.bindings.https.url}/.well-known/openid-configuration");
        env["OPENID_REDIRECT_URI"].ShouldBe("http://localhost:8180/oauth/oidc/callback");
        env["WEBUI_URL"].ShouldBe("http://localhost:8180");

        var auth = await Environment(app.AuthServer.Resource);
        var client = auth.Single(e => e.Value == "demo-openwebui").Key.Replace("__ClientId", "", StringComparison.Ordinal);
        auth[$"{client}__SystemAdministratorsOnly"].ShouldBe("true");
        auth[$"{client}__RedirectUris__0"].ShouldBe("{openwebui.bindings.http.url}/oauth/oidc/callback");
    }

    [Fact]
    public void Published_apps_take_their_tools_along_unless_switched_off()
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", $"--{CoworkeeApp.DeployToolsSetting}:webdatastudio=false"]);
        var app = builder.AddCoworkeeApp("demo", o => o.UseGrafana().UseWebDataStudio());

        app.AddProjects();

        builder.Resources.OfType<GrafanaResource>().ShouldHaveSingleItem();
        builder.Resources.OfType<WebDataStudioResource>().ShouldBeEmpty();
        app.Deploys(CoworkeeTools.Mailpit).ShouldBeFalse();
        app.Deploys(CoworkeeTools.Ollama).ShouldBeTrue();
        app.Deploys(CoworkeeTools.AspireUI).ShouldBeTrue();
    }

    [Theory]
    [InlineData("azure")]
    [InlineData("k8s")]
    public void AspireUI_needs_the_Docker_of_a_compose_host_and_stays_out_of_Azure_and_Kubernetes(string target)
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", $"--{CoworkeeApp.DeployTargetSetting}={target}"]);

        var app = builder.AddCoworkeeApp("demo");

        app.Deploys(CoworkeeTools.AspireUI).ShouldBeFalse();
        app.Deploys(CoworkeeTools.Grafana).ShouldBeTrue();
    }

    private static T Allocated<T>(T resource)
        where T : IResourceWithEndpoints
    {
        foreach (var endpoint in resource.Annotations.OfType<EndpointAnnotation>())
        {
            endpoint.AllocatedEndpoint ??= new AllocatedEndpoint(endpoint, "localhost", 8180);
        }

        return resource;
    }

    private static async Task<Dictionary<string, string?>> Environment(IResourceWithEnvironment resource) =>
        await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
}

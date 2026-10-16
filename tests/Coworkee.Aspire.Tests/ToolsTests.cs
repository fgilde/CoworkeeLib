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
    public async Task With_keycloak_both_sign_in_through_the_realm_and_keep_their_own_login()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => { o.Administrator = Admin; o.UseKeycloak().UseGrafana().UseWebDataStudio(); });

        app.AddProjects();

        app.KeycloakClients.Select(c => c.ClientId).ShouldBe(["demo-auth", "demo-grafana", "demo-webdatastudio"]);
        var keycloak = await Environment(Allocated(app.Keycloak!.Resource));
        keycloak["KC_HOSTNAME"].ShouldBe("http://localhost:8180");
        keycloak["KC_HOSTNAME_BACKCHANNEL_DYNAMIC"].ShouldBe("true");

        Allocated(app.Grafana!.Resource);
        var grafana = await Environment(app.Grafana.Resource);
        grafana["GF_AUTH_GENERIC_OAUTH_AUTH_URL"].ShouldBe("http://localhost:8180/realms/demo/protocol/openid-connect/auth");
        grafana["GF_AUTH_GENERIC_OAUTH_TOKEN_URL"].ShouldBe("http://keycloak:8080/realms/demo/protocol/openid-connect/token");
        grafana["GF_AUTH_GENERIC_OAUTH_CLIENT_SECRET"].ShouldBe("{demo-grafana-secret.value}");
        grafana["GF_AUTH_GENERIC_OAUTH_ROLE_ATTRIBUTE_PATH"].ShouldStartWith("email == 'admin@demo.test' && 'GrafanaAdmin' || contains(roles[*]");
        grafana["GF_AUTH_DISABLE_LOGIN_FORM"].ShouldBe("false");
        grafana["GF_SECURITY_DISABLE_INITIAL_ADMIN_CREATION"].ShouldBe("false");

        var studio = await Environment(Allocated(app.WebDataStudio!.Resource));
        studio["WDS_OIDC_AUTHORITY"].ShouldBe("http://keycloak:8080/realms/demo");
        studio["WDS_OIDC_CLIENT_SECRET"].ShouldBe("{demo-webdatastudio-secret.value}");
        studio["WDS_OIDC_ADMINS"].ShouldBe("admin@demo.test,Administrator");
        studio["WDS_USER"].ShouldBe(Admin.UserName);
        (await app.KeycloakClients[2].RedirectUris[0].GetValueAsync(TestContext.Current.CancellationToken)).ShouldBe("http://localhost:8180/signin-oidc");
    }

    [Fact]
    public async Task Sql_server_is_the_database_of_both()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseSqlServer().UseGrafana().UseWebDataStudio());

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldNotContain("postgres-exporter");
        (await Environment(Allocated(app.Grafana!.Resource)))["GF_DS_APP_DATABASE_PASSWORD"].ShouldBe("{sqlserver-password.value}");
        var studio = await Environment(app.WebDataStudio!.Resource);
        studio["WDS_CONN_DEMO"].ShouldContain("demo");
        studio["WDS_CONN_DEMO_ENGINE"].ShouldBe("sqlserver");
        studio["WDS_USER"].ShouldBe("admin");
        studio["WDS_PASSWORD"].ShouldBe("{demo-admin-password.value}");
    }

    [Fact]
    public async Task Aspireui_signs_in_through_the_realm_with_the_roles_of_the_app()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseKeycloak());
        app.AddProjects();

        var aspireUi = builder.AddAspireUI().WithCoworkeeSignOn(app);

        app.KeycloakClients.Last().ClientId.ShouldBe("demo-aspireui");
        (await Environment(Allocated(app.Keycloak!.Resource)))["KC_HOSTNAME"].ShouldBe("http://localhost:8180");
        var env = await Environment(Allocated(aspireUi.Resource));
        env["ASPIREUI_SET_OidcAuthority"].ShouldBe("http://keycloak:8080/realms/demo");
        env["ASPIREUI_SET_OidcClientId"].ShouldBe("demo-aspireui");
        env["ASPIREUI_SET_OidcClientSecret"].ShouldBe("{demo-aspireui-secret.value}");
        env["ASPIREUI_SET_OidcGroupsClaim"].ShouldBe("roles");
        env["ASPIREUI_SET_OidcAdminGroup"].ShouldBe("Administrator");
        env["ASPIREUI_SET_FORCE"].ShouldBe("true");
        (await app.KeycloakClients.Last().RedirectUris[0].GetValueAsync(TestContext.Current.CancellationToken)).ShouldBe("http://localhost:8180/api/auth/sso/callback");
    }

    [Fact]
    public async Task Sqlite_is_mounted_into_the_studio_and_left_out_of_grafana()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseSqlite().UseGrafana().UseWebDataStudio());

        app.AddProjects();

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

using Aspire.Hosting;

namespace Coworkee.Aspire.Tests;

public sealed class PublishTests
{
    [Fact]
    public async Task Docker_compose_writes_a_compose_file_with_services_and_containers()
    {
        var output = Directory.CreateTempSubdirectory("coworkee-compose").FullName;
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", "--step", "publish", "--output-path", output]);
        builder.AddCoworkeeApp("demo", o => o.PublishToDockerCompose()).AddProjects();

        using var app = builder.Build();
        await app.RunAsync(TestContext.Current.CancellationToken);

        var compose = await File.ReadAllTextAsync(Path.Combine(output, "docker-compose.yaml"), TestContext.Current.CancellationToken);
        compose.ShouldContain("demo-web:");
        compose.ShouldContain("postgres:");
        compose.ShouldContain("ConnectionStrings__demo:");
        compose.ShouldContain("service_completed_successfully");
    }

    [Fact]
    public async Task A_published_app_takes_public_addresses_secrets_the_administrator_and_the_mail_server_from_parameters()
    {
        var (compose, env) = await PublishAsync("compose", _ => { });

        compose.ShouldContain("Coworkee__Bff__Authority: \"${DEMO_AUTH_URL}\"");
        compose.ShouldContain("Coworkee__Auth__Issuer: \"${DEMO_AUTH_URL}\"");
        compose.ShouldContain("Coworkee__Auth__Clients__0__RedirectUris__0: \"${DEMO_WEB_URL}/signin-oidc\"");
        compose.ShouldContain("Coworkee__Auth__SigningCertificate__Base64: \"${DEMO_SIGNING_CERTIFICATE}\"");
        compose.ShouldContain("Coworkee__SetupToken: \"${DEMO_SETUP_TOKEN}\"");
        compose.ShouldContain("Coworkee__InitialAdmin__Email: \"${DEMO_ADMIN_EMAIL}\"");
        compose.ShouldContain("Coworkee__InitialAdmin__Password: \"${DEMO_ADMIN_PASSWORD}\"");
        compose.ShouldContain("Coworkee__Settings__Defaults__Mail.Smtp.Host: \"${DEMO_SMTP_HOST}\"");
        compose.ShouldContain("Coworkee__Settings__Defaults__Mail.Smtp.Password: \"${DEMO_SMTP_PASSWORD}\"");
        compose.ShouldContain("Coworkee__Settings__Defaults__Mail.From: \"${DEMO_SMTP_FROM}\"");
        compose.ShouldContain("Coworkee__Storage__FileSystem__Root: \"/home/app/blobs\"");
        compose.ShouldContain("source: \"demo-files\"");
        compose.ShouldContain("source: \"demo-postgres-data\"");
        compose.ShouldContain("127.0.0.1:8080:8080");
        compose.ShouldNotContain("DevelopmentCertificates");
        compose.ShouldNotContain("axllent/mailpit");
        env.ShouldContain("DEMO_WEB_URL=");
    }

    [Fact]
    public async Task The_tools_go_along_with_volumes_public_addresses_and_a_realm_the_migrations_reconcile()
    {
        var (compose, env) = await PublishAsync("compose", o => o.UseKeycloak().UseLibreTranslate(["de"]).UseGrafana().UseWebDataStudio());

        compose.ShouldContain("quay.io/keycloak/keycloak");
        compose.ShouldContain("KC_HOSTNAME: \"${DEMO_KEYCLOAK_URL}\"");
        compose.ShouldContain("KC_PROXY_HEADERS: \"xforwarded\"");
        compose.ShouldContain("127.0.0.1:8090:8080");
        compose.ShouldContain("source: \"demo-keycloak-data\"");
        compose.ShouldContain("Coworkee__Auth__External__Providers__keycloak__Authority: \"http://keycloak:8080/realms/demo\"");
        compose.ShouldContain("Coworkee__Auth__External__Providers__keycloak__BrowserUrl: \"${DEMO_KEYCLOAK_URL}\"");
        compose.ShouldContain("Coworkee__KeycloakRealm__Url: \"http://keycloak:8080\"");
        compose.ShouldContain("Coworkee__KeycloakRealm__Clients__0__RedirectUris__0: \"${DEMO_AUTH_URL}/signin-keycloak\"");
        compose.ShouldContain("Coworkee__KeycloakRealm__Clients__1__RedirectUris__0: \"${DEMO_GRAFANA_URL}/login/generic_oauth\"");
        compose.ShouldContain("Coworkee__KeycloakRealm__Users__0__Email: \"${DEMO_ADMIN_EMAIL}\"");
        compose.ShouldContain("GF_SERVER_ROOT_URL: \"${DEMO_GRAFANA_URL}\"");
        compose.ShouldContain("GF_AUTH_GENERIC_OAUTH_AUTH_URL: \"${DEMO_KEYCLOAK_URL}/realms/demo/protocol/openid-connect/auth\"");
        compose.ShouldContain("GF_SECURITY_ADMIN_USER: \"${DEMO_ADMIN_EMAIL}\"");
        compose.ShouldContain("127.0.0.1:8091:3000");
        compose.ShouldContain("source: \"demo-prometheus-data\"");
        compose.ShouldContain("GRAFANA_OTLP_ENDPOINT: \"http://otel-collector:4318\"");
        compose.ShouldContain("OTEL_METRIC_EXPORT_INTERVAL: \"10000\"");
        compose.ShouldContain("WDS_USER: \"${DEMO_ADMIN_EMAIL}\"");
        compose.ShouldContain("127.0.0.1:8092:8080");
        compose.ShouldContain("source: \"demo-webdatastudio-data\"");
        compose.ShouldContain("libretranslate/libretranslate");
        compose.ShouldContain("source: \"demo-libretranslate-models\"");
        compose.ShouldContain("Coworkee__Services__grafana__Url: \"${DEMO_GRAFANA_URL}\"");
        env.ShouldContain("GRAFANA_IMAGE=");
    }

    [Fact]
    public async Task Each_tool_can_be_left_out_and_mailpit_comes_along_when_switched_on()
    {
        var (compose, _) = await PublishAsync("compose", o => o.UseKeycloak().UseGrafana().UseWebDataStudio().DeployTool(CoworkeeTools.WebDataStudio, false),
            $"--{CoworkeeApp.DeployToolsSetting}:grafana=false", $"--{CoworkeeApp.DeployToolsSetting}:mailpit=true");

        compose.ShouldContain("quay.io/keycloak/keycloak");
        compose.ShouldNotContain("GRAFANA_IMAGE");
        compose.ShouldNotContain("webdatastudio");
        compose.ShouldContain("axllent/mailpit");
        compose.ShouldContain("127.0.0.1:8094:8025");
        compose.ShouldContain("MP_UI_AUTH: \"${DEMO_ADMIN_EMAIL}:${DEMO_ADMIN_PASSWORD}\"");
        compose.ShouldContain("Coworkee__Settings__Defaults__Mail.Smtp.Host: \"mail\"");
        compose.ShouldContain("Coworkee__Settings__Defaults__Mail.Smtp.Port: \"1025\"");
        compose.ShouldNotContain("DEMO_SMTP_HOST");
    }

    [Theory]
    [InlineData("azure", "main.bicep")]
    [InlineData("k8s", "Chart.yaml")]
    public async Task The_deploy_target_picks_the_publisher_and_takes_the_tools_along(string target, string file)
    {
        var output = await PublishFolderAsync(target, o => o.UseKeycloak().UseLibreTranslate(["de"]).UseGrafana().UseWebDataStudio());

        File.Exists(Path.Combine(output, file)).ShouldBeTrue();
        var written = string.Join('\n', Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        foreach (var tool in new[] { "keycloak", "grafana", "webdatastudio", "demo-translate", "demo_grafana_url", "demo_keycloak_url" })
        {
            written.ShouldContain(tool);
        }
    }

    [Fact]
    public void Runs_without_compose_environment_unless_asked()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCoworkeeApp("demo");

        builder.Resources.ShouldBeEmpty();
    }

    private static async Task<(string Compose, string Env)> PublishAsync(string target, Action<CoworkeeAppOptions> configure, params string[] args)
    {
        var output = await PublishFolderAsync(target, configure, args);
        return (await File.ReadAllTextAsync(Path.Combine(output, "docker-compose.yaml")), await File.ReadAllTextAsync(Path.Combine(output, ".env")));
    }

    private static async Task<string> PublishFolderAsync(string target, Action<CoworkeeAppOptions> configure, params string[] args)
    {
        var output = Directory.CreateTempSubdirectory("coworkee-publish").FullName;
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", "--step", "publish", "--output-path", output, $"--{CoworkeeApp.DeployTargetSetting}={target}", .. args]);
        var app = builder.AddCoworkeeApp("demo", configure);
        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();

        using var host = builder.Build();
        await host.RunAsync(TestContext.Current.CancellationToken);
        return output;
    }
}

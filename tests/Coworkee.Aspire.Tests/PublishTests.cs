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
    public async Task A_published_app_takes_public_addresses_secrets_and_volumes_and_leaves_the_development_tools_out()
    {
        var (compose, env) = await PublishAsync("compose", o => o.UseKeycloak().UseLibreTranslate(["de"]));

        compose.ShouldContain("Coworkee__Bff__Authority: \"${DEMO_AUTH_URL}\"");
        compose.ShouldContain("Coworkee__Auth__Issuer: \"${DEMO_AUTH_URL}\"");
        compose.ShouldContain("Coworkee__Auth__Clients__0__RedirectUris__0: \"${DEMO_WEB_URL}/signin-oidc\"");
        compose.ShouldContain("Coworkee__Auth__SigningCertificate__Base64: \"${DEMO_SIGNING_CERTIFICATE}\"");
        compose.ShouldContain("Coworkee__SetupToken: \"${DEMO_SETUP_TOKEN}\"");
        compose.ShouldContain("Coworkee__Storage__FileSystem__Root: \"/home/app/blobs\"");
        compose.ShouldContain("source: \"demo-files\"");
        compose.ShouldContain("source: \"demo-postgres-data\"");
        compose.ShouldContain("127.0.0.1:8080:8080");
        compose.ShouldNotContain("DevelopmentCertificates");
        compose.ShouldNotContain("keycloak");
        compose.ShouldNotContain("axllent/mailpit");
        compose.ShouldNotContain("libretranslate/libretranslate");
        env.ShouldContain("DEMO_WEB_URL=");
    }

    [Theory]
    [InlineData("azure", "main.bicep")]
    [InlineData("k8s", "Chart.yaml")]
    public async Task The_deploy_target_picks_the_publisher(string target, string file)
    {
        var output = await PublishFolderAsync(target, _ => { });

        File.Exists(Path.Combine(output, file)).ShouldBeTrue();
    }

    [Fact]
    public void Runs_without_compose_environment_unless_asked()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCoworkeeApp("demo");

        builder.Resources.ShouldBeEmpty();
    }

    private static async Task<(string Compose, string Env)> PublishAsync(string target, Action<CoworkeeAppOptions> configure)
    {
        var output = await PublishFolderAsync(target, configure);
        return (await File.ReadAllTextAsync(Path.Combine(output, "docker-compose.yaml")), await File.ReadAllTextAsync(Path.Combine(output, ".env")));
    }

    private static async Task<string> PublishFolderAsync(string target, Action<CoworkeeAppOptions> configure)
    {
        var output = Directory.CreateTempSubdirectory("coworkee-publish").FullName;
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish", "--step", "publish", "--output-path", output, $"--{CoworkeeApp.DeployTargetSetting}={target}"]);
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

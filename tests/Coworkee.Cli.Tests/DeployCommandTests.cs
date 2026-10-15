using System.Buffers.Text;
using System.Security.Cryptography.X509Certificates;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli.Tests;

public sealed class DeployCommandTests
{
    private const string Env = """
        # Parameter postgres-password
        POSTGRES_PASSWORD=

        # Container image name for shop-api
        SHOP_API_IMAGE=

        # Default container port for shop-api
        SHOP_API_PORT=

        # Parameter shop-signing-certificate
        SHOP_SIGNING_CERTIFICATE=

        # Parameter shop-setup-token
        SHOP_SETUP_TOKEN=

        # Parameter shop-web-url
        SHOP_WEB_URL=

        # Parameter shop-push-public-key
        SHOP_PUSH_PUBLIC_KEY=

        # Parameter shop-push-private-key
        SHOP_PUSH_PRIVATE_KEY=
        """;

    [Fact]
    public void Deploy_publishes_the_app_host_for_the_target_into_deploy_by_default()
    {
        using var folder = new TempFolder();
        var appHost = folder.File("src/Shop.AppHost/Shop.AppHost.csproj");
        var api = folder.File("src/Shop.Api/Shop.Api.csproj");
        folder.File("Shop.slnx");
        var solution = Solution.Find(folder.Path);

        var output = DeployCommand.OutputFolder(solution, "k8s", null);

        output.ShouldBe(Path.Combine(folder.Path, "deploy", "k8s"));
        DeployCommand.OutputFolder(solution, "k8s", "out").ShouldBe(Path.GetFullPath("out"));
        DeployCommand.PublishArgs(solution, "k8s", output).ShouldBe(
        [
            "dotnet", "run", "--project", appHost, "--no-launch-profile", "--",
            "--operation", "publish", "--step", "publish", "--output-path", output, "--environment", "Production", "--Coworkee:Deploy:Target=k8s",
        ]);
        DeployCommand.BuildImageArgs(solution, "shop-api")[..3].ShouldBe(["dotnet", "publish", api]);
        DeployCommand.BuildImageArgs(solution, "shop-api").ShouldContain("-p:ContainerRepository=shop-api");
        Should.Throw<CliException>(() => DeployCommand.BuildImageArgs(solution, "shop-worker"));
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("deploy compose -o out --no-build")]
    [InlineData("deploy azure")]
    [InlineData("deploy k8s")]
    public void Deploy_accepts_the_targets(string args) => CliApp.Build().Parse(args).Errors.ShouldBeEmpty();

    [Fact]
    public void Deploy_rejects_unknown_targets() => CliApp.Build().Parse("deploy heroku").Errors.ShouldNotBeEmpty();

    [Fact]
    public void The_env_gets_secrets_ports_images_and_certificates_but_no_guessed_addresses()
    {
        var values = ComposeEnv.Values(ComposeEnv.Fill(Env, new Dictionary<string, string>(), "Shop"));

        values["POSTGRES_PASSWORD"].Length.ShouldBe(40);
        values["SHOP_SETUP_TOKEN"].Length.ShouldBe(40);
        values["SHOP_API_IMAGE"].ShouldBe("shop-api:latest");
        values["SHOP_API_PORT"].ShouldBe("8080");
        values["SHOP_WEB_URL"].ShouldBeEmpty();
        using var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(values["SHOP_SIGNING_CERTIFICATE"]), null);
        certificate.HasPrivateKey.ShouldBeTrue();
        ComposeEnv.Images(Env).ShouldBe(["shop-api"]);
        Base64Url.DecodeFromChars(values["SHOP_PUSH_PUBLIC_KEY"]).Length.ShouldBe(65);
        Base64Url.DecodeFromChars(values["SHOP_PUSH_PRIVATE_KEY"]).Length.ShouldBe(32);
    }

    [Fact]
    public void Values_of_an_earlier_run_and_set_values_are_kept()
    {
        var env = Env.Replace("SHOP_WEB_URL=", "SHOP_WEB_URL=https://shop.example.com");

        var values = ComposeEnv.Values(ComposeEnv.Fill(env, new Dictionary<string, string> { ["POSTGRES_PASSWORD"] = "kept" }, "Shop"));

        values["POSTGRES_PASSWORD"].ShouldBe("kept");
        values["SHOP_WEB_URL"].ShouldBe("https://shop.example.com");
    }

    [Fact]
    public void The_helm_chart_reads_dotted_keys_with_index_and_runs_the_migrations_as_a_job()
    {
        KubernetesChart.IndexDottedKeys("a: \"{{ .Values.config.shop_api.Coworkee__Settings__Defaults__Ai.Provider }}\" b: \"{{ .Values.config.shop_api.HTTP_PORTS }}\"")
            .ShouldBe("a: \"{{ index .Values.config.shop_api \"Coworkee__Settings__Defaults__Ai.Provider\" }}\" b: \"{{ .Values.config.shop_api.HTTP_PORTS }}\"");

        var job = KubernetesChart.AsJob("""
            ---
            apiVersion: "apps/v1"
            kind: "Deployment"
            metadata:
              name: "shop-migrations-deployment"
            spec:
              template:
                metadata:
                  labels:
                    app: "x"
                spec:
                  containers:
                    - name: "shop-migrations"
              selector:
                matchLabels:
                  app: "x"
              replicas: 1
            """);

        job.ShouldContain("kind: \"Job\"");
        job.ShouldContain("name: \"shop-migrations-job\"");
        job.ShouldContain("restartPolicy: \"OnFailure\"");
        job.ShouldContain("helm.sh/hook: \"post-install,post-upgrade\"");
        job.ShouldNotContain("replicas");
        job.ShouldNotContain("selector");
        Should.Throw<CliException>(() => KubernetesChart.AsJob("kind: Service"));
    }
}

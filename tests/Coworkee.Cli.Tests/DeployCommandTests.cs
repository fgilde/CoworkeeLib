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

        # Parameter shop-admin-email
        SHOP_ADMIN_EMAIL=

        # Parameter shop-admin-password
        SHOP_ADMIN_PASSWORD=

        # Parameter shop-smtp-port
        SHOP_SMTP_PORT=

        # Parameter shop-smtp-password
        SHOP_SMTP_PASSWORD=

        # Container image name for grafana
        GRAFANA_IMAGE=
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

    [Fact]
    public void Tool_switches_reach_the_app_host_and_the_administrator_and_mail_server_are_asked_for()
    {
        using var folder = new TempFolder();
        var appHost = folder.File("src/Shop.AppHost/Shop.AppHost.csproj");
        folder.File("Shop.slnx");
        var solution = Solution.Find(folder.Path);

        DeployCommand.PublishArgs(solution, "compose", "out", DeployCommand.ToolSwitches(["grafana"], ["mailpit"]))[^2..]
            .ShouldBe(["--Coworkee:Deploy:Tools:grafana=false", "--Coworkee:Deploy:Tools:mailpit=true"]);

        var asked = new List<string>();
        var known = DeploySummary.Known(["SHOP_ADMIN_EMAIL", "SHOP_SMTP_HOST", "SHOP_SMTP_PORT", "POSTGRES_PASSWORD"], new Dictionary<string, string> { ["SHOP_SMTP_PORT"] = "587" },
            k => k == "SHOP_SMTP_HOST" ? "smtp.example.com" : null, null, k => { asked.Add(k); return "ada@example.com"; });

        known["SHOP_ADMIN_EMAIL"].ShouldBe("ada@example.com");
        known["SHOP_SMTP_HOST"].ShouldBe("smtp.example.com");
        known["SHOP_SMTP_PORT"].ShouldBe("587");
        asked.ShouldBe(["SHOP_ADMIN_EMAIL"]);
        DeploySummary.Known(["SHOP_ADMIN_EMAIL"], new Dictionary<string, string>(), _ => null, "grace@example.com", null)["SHOP_ADMIN_EMAIL"].ShouldBe("grace@example.com");
    }

    [Fact]
    public void The_summary_lists_each_loopback_port_with_its_public_address_and_how_to_sign_in()
    {
        const string compose = """
            services:
              keycloak:
                ports:
                  - "127.0.0.1:8090:8080"
              shop-web:
                ports:
                  - "127.0.0.1:8082:8080"
              postgres:
                expose:
                  - "5432"
            """;
        var values = new Dictionary<string, string>
        {
            ["SHOP_WEB_URL"] = "https://shop.example.com", ["SHOP_KEYCLOAK_URL"] = "", ["SHOP_ADMIN_EMAIL"] = "ada@example.com", ["SHOP_ADMIN_PASSWORD"] = "Pw1-x", ["KEYCLOAK_PASSWORD"] = "k",
        };

        var lines = DeploySummary.Addresses(compose, values, "Shop").ToList();

        lines.Count.ShouldBe(2);
        lines[0].ShouldContain("127.0.0.1:8090");
        lines[0].ShouldContain("SHOP_KEYCLOAK_URL=(not set yet)");
        lines[1].ShouldContain("SHOP_WEB_URL=https://shop.example.com");
        var credentials = string.Join(' ', DeploySummary.Credentials(values));
        credentials.ShouldContain("ada@example.com");
        credentials.ShouldContain("Pw1-x");
        credentials.ShouldContain("KEYCLOAK_PASSWORD");
        credentials.ShouldNotContain("setup token");
        string.Join(' ', DeploySummary.Credentials(new Dictionary<string, string> { ["SHOP_ADMIN_EMAIL"] = "", ["SHOP_SETUP_TOKEN"] = "tok-1" }))
            .ShouldContain("setup token tok-1 (SHOP_SETUP_TOKEN");
    }

    [Fact]
    public void The_public_addresses_of_a_bicep_or_helm_output_are_listed()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("grafana/grafana.bicep"), "param shop_grafana_url_value string param shop_keycloak_url_value string");
        File.WriteAllText(folder.File("values.yaml"), "shop_web_url: \"\"");

        DeploySummary.PublicAddresses(folder.Path).ShouldBe(["shop_grafana_url", "shop_keycloak_url", "shop_web_url"]);
    }

    [Theory]
    [InlineData("deploy compose --without grafana webdatastudio --with mailpit --admin-email ada@example.com")]
    [InlineData("deploy")]
    [InlineData("deploy compose -o out --no-build")]
    [InlineData("deploy azure")]
    [InlineData("deploy k8s")]
    public void Deploy_accepts_the_targets(string args) => CliApp.Build().Parse(args).Errors.ShouldBeEmpty();

    [Fact]
    public void Deploy_rejects_unknown_targets() => CliApp.Build().Parse("deploy heroku").Errors.ShouldNotBeEmpty();

    [Fact]
    public void Deploy_rejects_unknown_tools() => CliApp.Build().Parse("deploy --without jenkins").Errors.ShouldNotBeEmpty();

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
        ComposeEnv.Images(Env).ShouldBe(["shop-api", "grafana"]);
        values["GRAFANA_IMAGE"].ShouldBe("shop-grafana:latest");
        values["SHOP_ADMIN_EMAIL"].ShouldBe("admin@shop.local");
        values["SHOP_ADMIN_PASSWORD"].ShouldSatisfyAllConditions(p => p.Any(char.IsUpper).ShouldBeTrue(), p => p.Any(char.IsLower).ShouldBeTrue(),
            p => p.Any(char.IsDigit).ShouldBeTrue(), p => p.Any(c => !char.IsLetterOrDigit(c)).ShouldBeTrue());
        values["SHOP_SMTP_PORT"].ShouldBeEmpty();
        values["SHOP_SMTP_PASSWORD"].ShouldBeEmpty();
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

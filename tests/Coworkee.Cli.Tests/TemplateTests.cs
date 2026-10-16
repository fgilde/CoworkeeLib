using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class TemplateTests(TemplateFixture template)
{
    [Fact]
    public async Task Without_samples_the_app_is_renamed_and_references_the_packed_version()
    {
        var app = await template.CreateAsync("bare", "--samples", "false", "--title", "My Shop");

        File.Exists(Path.Combine(app, "Shop.slnx")).ShouldBeTrue();
        File.Exists(Path.Combine(app, "src", "Shop.AppHost", "Shop.AppHost.csproj")).ShouldBeTrue();
        Directory.Exists(Path.Combine(app, "src", "Shop.Catalog")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(app, "Shop.slnx")).ShouldNotContain("Catalog");
        File.ReadAllText(Path.Combine(app, "Directory.Packages.props")).ShouldContain($"<CoworkeeVersion>{template.Version}</CoworkeeVersion>");
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldContain("options.DisplayName = \"My Shop\";");
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldNotContain("Keycloak");
        File.ReadAllText(Path.Combine(app, "src", "Shop.Migrations", "DemoUsers.cs")).ShouldMatch("\"Cw1!(?i:[0-9a-f]{32})\"");
        File.ReadAllText(Path.Combine(app, "LOGIN.md")).ShouldMatch("`Cw1!(?i:[0-9a-f]{32})`");
        File.ReadAllText(Path.Combine(app, "quickrun.yml")).ShouldContain("run: dotnet run --project src/Shop.AppHost");
        File.ReadAllText(Path.Combine(app, "quickrun.yml")).ShouldContain("name: \"My Shop\"");
        File.ReadAllText(Path.Combine(app, "README.md")).ShouldContain("## QuickRun");
        File.ReadAllText(Path.Combine(app, "README.md")).ShouldNotContain("quickrun.org/badge.svg");
        File.ReadAllText(Path.Combine(app, "src", "Shop.Web", "Program.cs")).ShouldContain("app.UseWebAssemblyDebugging();");
        ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task By_default_the_samples_and_tests_are_included()
    {
        var app = await template.CreateAsync("full", "--keycloak", "true", "--repository", "acme/shop");

        File.ReadAllText(Path.Combine(app, "Shop.slnx")).ShouldContain("src/Shop.Catalog/Shop.Catalog.csproj");
        File.ReadAllText(Path.Combine(app, "Shop.slnx")).ShouldContain("tests/Shop.Api.Tests/Shop.Api.Tests.csproj");
        File.Exists(Path.Combine(app, "src", "Shop.Documents", "Shop.Documents.csproj")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldContain("options.DisplayName = \"Shop\";");
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldContain("UseKeycloak");
        File.ReadAllText(Path.Combine(app, "README.md")).ShouldContain("[![QuickRun](https://quickrun.org/badge.svg)](https://quickrun.org/run?repo=acme/shop)");
        ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task Generated_app_builds_against_the_local_feed()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("build", "--samples", "false");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");

        await Dotnet.RunAsync(app, "build", "Shop.slnx");
    }

    [Fact]
    public async Task Generated_app_with_webhooks_and_samples_builds_and_its_migration_covers_the_model()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("build-webhooks", "--webhooks", "true");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        await Dotnet.RunAsync(app, "restore", "Shop.slnx");
        var solution = Solution.Find(app);
        await Dotnet.RunAsync(app, MigrationsCommand.AddArgs(solution, NewCommand.MigrationName(solution, new AppSpec { Name = "Shop" })!)[1..]);

        await Dotnet.RunAsync(app, "build", "Shop.slnx", "--no-restore");
        await Dotnet.RunAsync(app, "test", "--project", "tests/Shop.Migrations.Tests", "--no-build");
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("sqlServer")]
    [InlineData("sqlite")]
    public async Task Generated_app_builds_with_each_database_and_its_migration_covers_the_model(string database)
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync($"build-{database}", "--database", database);
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        await Dotnet.RunAsync(app, "restore", "Shop.slnx");
        var solution = Solution.Find(app);
        if (NewCommand.MigrationName(solution, new AppSpec { Name = "Shop" }) is { } migration)
        {
            await Dotnet.RunAsync(app, MigrationsCommand.AddArgs(solution, migration)[1..]);
        }

        await Dotnet.RunAsync(app, "build", "Shop.slnx", "--no-restore");
        await Dotnet.RunAsync(app, "test", "--project", "tests/Shop.Migrations.Tests", "--no-build");
    }

    [Fact]
    public async Task Generated_app_publishes_a_compose_deployment_with_its_tools()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to publish a generated app.");
        var app = await template.CreateAsync("deploy", "--samples", "false", "--keycloak", "true", "--grafana", "true", "--webDataStudio", "true",
            "--translationProvider", "libreTranslate", "--ai", "true", "--aiProvider", "ollama", "--aspireUI", "true");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        var solution = Solution.Find(app);
        var output = DeployCommand.OutputFolder(solution, "compose", null);

        await Dotnet.RunAsync(app, DeployCommand.PublishArgs(solution, "compose", output)[1..]);

        var compose = await File.ReadAllTextAsync(Path.Combine(output, "docker-compose.yaml"), TestContext.Current.CancellationToken);
        foreach (var service in new[] { "shop-migrations:", "shop-auth:", "shop-api:", "shop-web:", "postgres:", "redis:", "keycloak:", "grafana:", "webdatastudio:", "shop-translate:", "ollama:", "aspireui:" })
        {
            compose.ShouldContain(service);
        }

        compose.ShouldContain("Coworkee__Bff__Authority: \"${SHOP_AUTH_URL}\"");
        compose.ShouldContain("condition: \"service_completed_successfully\"");
        compose.ShouldContain("Coworkee__KeycloakRealm__Users__0__Email: \"${SHOP_ADMIN_EMAIL}\"");
        compose.ShouldContain("source: \"shop-ollama-data\"");
        compose.ShouldContain("ASPIREUI_ADMIN_USERNAME: \"${SHOP_ADMIN_EMAIL}\"");
        compose.ShouldNotContain("axllent/mailpit");
        compose.ShouldNotContain("DevelopmentCertificates");
        // no development password reaches a deployment
        compose.ShouldNotContain("SEED_PASSWORD");
        foreach (System.Text.RegularExpressions.Match password in System.Text.RegularExpressions.Regex.Matches(await File.ReadAllTextAsync(Path.Combine(app, "src", "Shop.Migrations", "DemoUsers.cs")), "\"(Cw1[^\"]+)\""))
        {
            compose.ShouldNotContain(password.Groups[1].Value);
        }

        var published = await File.ReadAllTextAsync(Path.Combine(output, ".env"), TestContext.Current.CancellationToken);
        var env = ComposeEnv.Values(ComposeEnv.Fill(published, new Dictionary<string, string>(), "Shop"));
        env.Keys.ShouldContain("SHOP_ADMIN_EMAIL");
        env.Keys.ShouldContain("SHOP_SMTP_HOST");
        env.Keys.ShouldContain("SHOP_GRAFANA_URL");
        env.Keys.ShouldContain("SHOP_KEYCLOAK_URL");
        env["GRAFANA_IMAGE"].ShouldBe("shop-grafana:latest");
        foreach (var image in ComposeEnv.Images(published))
        {
            Path.Exists(image.StartsWith("shop-", StringComparison.Ordinal) ? DeployCommand.BuildImageArgs(solution, image)[2] : DeployCommand.BuildToolImageArgs(solution, image, "x")[^1])
                .ShouldBeTrue(image);
        }
    }

    internal static void ShouldHaveNoLeftovers(string app)
    {
        foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories))
        {
            file.ShouldNotContain("MyApp");
            var text = File.ReadAllText(file);
            text.ShouldNotContain("MyApp", customMessage: file);
            text.ShouldNotContain("myapp", customMessage: file);
            text.ShouldNotContain("COWORKEE_", customMessage: file);
            text.ShouldNotContain("SEED_PASSWORD", customMessage: file);
            text.ShouldNotContain("#if (", customMessage: file);
        }
    }
}

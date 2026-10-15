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
    public async Task Generated_app_publishes_a_compose_deployment_without_development_tools()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to publish a generated app.");
        var app = await template.CreateAsync("deploy", "--samples", "false", "--keycloak", "true");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        var solution = Solution.Find(app);
        var output = DeployCommand.OutputFolder(solution, "compose", null);

        await Dotnet.RunAsync(app, DeployCommand.PublishArgs(solution, "compose", output)[1..]);

        var compose = await File.ReadAllTextAsync(Path.Combine(output, "docker-compose.yaml"), TestContext.Current.CancellationToken);
        foreach (var service in new[] { "shop-migrations:", "shop-auth:", "shop-api:", "shop-web:", "postgres:", "redis:" })
        {
            compose.ShouldContain(service);
        }

        compose.ShouldContain("Coworkee__Bff__Authority: \"${SHOP_AUTH_URL}\"");
        compose.ShouldContain("condition: \"service_completed_successfully\"");
        compose.ShouldNotContain("keycloak");
        compose.ShouldNotContain("axllent/mailpit");
        compose.ShouldNotContain("libretranslate/libretranslate");
        compose.ShouldNotContain("DevelopmentCertificates");
        var env = ComposeEnv.Values(ComposeEnv.Fill(await File.ReadAllTextAsync(Path.Combine(output, ".env"), TestContext.Current.CancellationToken), new Dictionary<string, string>(), "Shop"));
        env.Keys.ShouldBe(["POSTGRES_PASSWORD", "REDIS_PASSWORD", "SHOP_API_IMAGE", "SHOP_AUTH_IMAGE", "SHOP_AUTH_URL", "SHOP_ENCRYPTION_CERTIFICATE",
            "SHOP_MIGRATIONS_IMAGE", "SHOP_SETUP_TOKEN", "SHOP_SIGNING_CERTIFICATE", "SHOP_WEB_IMAGE", "SHOP_WEB_URL"], ignoreOrder: true);
        ComposeEnv.Images(await File.ReadAllTextAsync(Path.Combine(output, ".env"), TestContext.Current.CancellationToken))
            .Select(i => DeployCommand.BuildImageArgs(solution, i)[2]).ShouldAllBe(p => File.Exists(p));
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

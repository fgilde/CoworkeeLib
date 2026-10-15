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

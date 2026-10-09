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
        File.ReadAllText(Path.Combine(app, "src", "Shop.Migrations", "DemoSeed.cs")).ShouldMatch("\"Cw1!(?i:[0-9a-f]{32})\"");
        ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task By_default_the_samples_and_tests_are_included()
    {
        var app = await template.CreateAsync("full", "--keycloak", "true");

        File.ReadAllText(Path.Combine(app, "Shop.slnx")).ShouldContain("src/Shop.Catalog/Shop.Catalog.csproj");
        File.ReadAllText(Path.Combine(app, "Shop.slnx")).ShouldContain("tests/Shop.Api.Tests/Shop.Api.Tests.csproj");
        File.Exists(Path.Combine(app, "src", "Shop.Documents", "Shop.Documents.csproj")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldContain("options.DisplayName = \"Shop\";");
        File.ReadAllText(Path.Combine(app, "src", "Shop.AppHost", "AppHost.cs")).ShouldContain("UseKeycloak");
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

    private static void ShouldHaveNoLeftovers(string app)
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

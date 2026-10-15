using Coworkee.Cli.Apps;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class AddTemplateTests(TemplateFixture template)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_app_without_app_json_reads_its_options_from_its_files()
    {
        string[] options =
        [
            "--samples", "false", "--reports", "false", "--chat", "false", "--files", "true", "--eventBus", "rabbitMq", "--database", "sqlite", "--keycloak", "true", "--redis", "false",
            "--languages", "en,fr", "--defaultLanguage", "fr", "--theme", "Forest", "--primaryColor", "#112233", "--title", "Acme Shop", "--aiProvider", "openAI",
            "--storage", "azureBlob", "--colorMode", "dark", "--registration", "false", "--translationProvider", "deepL", "--signInLayout", "Card",
        ];
        var app = await template.CreateAsync("infer", options);

        var inferred = SpecOptions.Resolve(ExistingApp.Infer(app));

        for (var i = 0; i < options.Length; i += 2)
        {
            inferred[options[i][2..]].ShouldBe(options[i + 1], options[i]);
        }

        inferred["grafana"].ShouldBe("true");
        inferred["auditing"].ShouldBe("true");
    }

    [Fact]
    public async Task Add_merges_the_option_into_an_app_the_user_changed_and_stores_its_options()
    {
        var app = await template.CreateAsync("add-reports", "--samples", "false", "--tests", "false", "--reports", "false");
        var solution = Solution.Find(app);
        var appHost = Path.Combine(app, "src/Shop.AppHost/AppHost.cs");
        File.WriteAllText(appHost, "// my own line\n" + File.ReadAllText(appHost));
        var navigation = Path.Combine(app, "src/Shop.Web.Client/Navigation/ShopNavigation.cs");
        var (current, inferred) = AppState.Read(solution);
        inferred.ShouldBeTrue();

        var dryRun = await AddCommand.RunAsync(solution, current, OptionChanges.Apply(current, ["reports"], remove: false), "AddReports", new ApplyOptions(DryRun: true, RequireClean: false, Hive: template.Hive, Migration: false), Ct);
        dryRun.ShouldBe(0);
        File.Exists(AppState.PathOf(app)).ShouldBeFalse();
        File.ReadAllText(navigation).ShouldNotContain("/reports");

        var exit = await AddCommand.RunAsync(solution, current, OptionChanges.Apply(current, ["reports"], remove: false), "AddReports", new ApplyOptions(DryRun: false, RequireClean: false, Hive: template.Hive, Migration: false), Ct);

        exit.ShouldBe(0);
        File.ReadAllText(Path.Combine(app, "src/Shop.Infrastructure/Shop.Infrastructure.csproj")).ShouldContain("\"Coworkee.Reporting\"");
        File.ReadAllText(navigation).ShouldContain("/reports");
        File.ReadAllText(appHost).ShouldStartWith("// my own line");
        SpecOptions.Resolve(AppState.Read(solution).Spec)["reports"].ShouldBe("true");
        Directory.EnumerateFiles(app, "*.rej", SearchOption.AllDirectories).ShouldBeEmpty();
        TemplateTests.ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task Remove_is_skipped_when_a_touched_file_conflicts()
    {
        var app = await template.CreateAsync("remove-chat", "--samples", "false", "--tests", "false");
        var solution = Solution.Find(app);
        var csproj = Path.Combine(app, "src/Shop.Infrastructure/Shop.Infrastructure.csproj");
        var text = File.ReadAllText(csproj).Replace("<CoworkeePackage Include=\"Coworkee.Chat\" />", "<CoworkeePackage Include=\"Coworkee.Chat\" /><!-- mine -->");
        File.WriteAllText(csproj, text);
        var current = AppState.Read(solution).Spec;

        var exit = await AddCommand.RunAsync(solution, current, OptionChanges.Apply(current, ["chat"], remove: true), "RemoveChat", new ApplyOptions(DryRun: false, RequireClean: true, Hive: template.Hive, Migration: false), Ct);

        exit.ShouldBe(1);
        File.ReadAllText(csproj).ShouldBe(text);
        File.Exists(AppState.PathOf(app)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_app_with_reports_added_later_builds_and_its_migrations_cover_the_model()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("add-build", "--samples", "false", "--reports", "false");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        await Dotnet.RunAsync(app, "restore", "Shop.slnx");
        var solution = Solution.Find(app);
        await Dotnet.RunAsync(app, MigrationsCommand.AddArgs(solution, "Initial")[1..]);
        var current = AppState.Read(solution).Spec;

        var exit = await AddCommand.RunAsync(solution, current, OptionChanges.Apply(current, ["reports"], remove: false), "AddReports", new ApplyOptions(DryRun: false, RequireClean: false, Hive: template.Hive), Ct);

        exit.ShouldBe(0);
        Directory.EnumerateFiles(Path.Combine(app, "src/Shop.Infrastructure/Migrations"), "*_AddReports.cs").ShouldHaveSingleItem();
        await Dotnet.RunAsync(app, "build", "Shop.slnx");
        await Dotnet.RunAsync(app, "test", "--project", "tests/Shop.Migrations.Tests", "--no-build", "--filter-class", "Shop.Migrations.Tests.ModelTests");
    }
}

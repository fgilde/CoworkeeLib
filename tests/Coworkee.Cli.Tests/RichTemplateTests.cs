using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class RichTemplateTests(TemplateFixture template)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Languages_color_mode_registration_and_tooling_reach_the_app()
    {
        var app = await template.CreateAsync("rich-options", "--samples", "false", "--languages", "en,de,fr", "--defaultLanguage", "de", "--colorMode", "dark",
            "--githubActions", "true", "--devcontainer", "true", "--translationProvider", "deepL", "--translationSource", "de");

        var infrastructure = Read(app, "src/Shop.Infrastructure/ShopInfrastructureModule.cs");
        infrastructure.ShouldContain("localization.Languages = [\"en\", \"de\", \"fr\"];");
        infrastructure.ShouldContain("localization.DefaultLanguage = \"de\";");
        var richHost = Read(app, "src/Shop.AppHost/AppHost.cs");
        richHost.ShouldContain("options.TranslationSource = \"de\";");
        richHost.ShouldContain("options.UseTranslation(\"deepL\", translation.GetValueOrDefault(\"key\")");
        richHost.ShouldContain("builder.AddParameter($\"translation-{n}\", secret: n == \"key\")");
        richHost.ShouldNotContain("UseLibreTranslate");
        var seed = Read(app, "src/Shop.Migrations/DemoSeed.cs");
        seed.ShouldContain("seed.Settings[\"Theme.Mode\"] = \"dark\";");
        seed.ShouldContain("seed.Settings[\"Account.AllowRegistration\"] = \"true\";");
        var workflow = Read(app, ".github/workflows/ci.yml");
        workflow.ShouldContain("dotnet build Shop.slnx -c Release");
        workflow.ShouldContain("dotnet test --solution Shop.slnx -c Release --no-build");
        Read(app, ".devcontainer/devcontainer.json").ShouldContain("\"postCreateCommand\": \"dotnet restore Shop.slnx\"");
        TemplateTests.ShouldHaveNoLeftovers(app);

        var german = await template.CreateAsync("german-default", "--defaultLanguage", "de", "--githubActions", "true", "--tests", "false");
        Read(german, "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldNotContain("localization.Languages");
        Read(german, "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldContain("localization.DefaultLanguage = \"de\";");
        Read(german, ".github/workflows/ci.yml").ShouldNotContain("dotnet test");
        var germanHost = Read(german, "src/Shop.AppHost/AppHost.cs");
        germanHost.ShouldContain("options.UseLibreTranslate([\"de\", \"de\"]);");
        germanHost.ShouldNotContain("TranslationSource");
        germanHost.ShouldNotContain("translation-{n}");
        TemplateTests.ShouldHaveNoLeftovers(german);

        var plain = await template.CreateAsync("plain-options", "--samples", "false", "--registration", "false");
        Read(plain, "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldNotContain("LocalizationOptions");
        Read(plain, "src/Shop.Migrations/DemoSeed.cs").ShouldNotContain("seed.Settings");
        Directory.Exists(Path.Combine(plain, ".github")).ShouldBeFalse();
        Directory.Exists(Path.Combine(plain, ".devcontainer")).ShouldBeFalse();
    }

    [Fact]
    public async Task Apps_get_the_texts_of_their_languages_only()
    {
        string[] Texts(string app) => Directory.Exists(Path.Combine(app, "src/Shop.Contracts/Localization")) ? [.. Directory.GetFiles(Path.Combine(app, "src/Shop.Contracts/Localization")).Select(Path.GetFileName).OfType<string>()] : [];

        Texts(await template.CreateAsync("texts-default", "--samples", "false", "--tests", "false")).ShouldBe(["de.json"]);
        Texts(await template.CreateAsync("texts-english", "--samples", "false", "--tests", "false", "--languages", "en")).ShouldBeEmpty();
        Texts(await template.CreateAsync("texts-regional", "--samples", "false", "--tests", "false", "--languages", "en,de-AT")).ShouldBe(["de.json"]);
        Texts(await template.CreateAsync("texts-chosen", "--samples", "false", "--tests", "false", "--languages", "en,fr,pt-BR")).Order().ShouldBe(["fr.json", "pt.json"]);
        Texts(await template.CreateAsync("texts-simplified", "--samples", "false", "--tests", "false", "--languages", "en,zh-CN")).ShouldBe(["zh.json"]);
        Texts(await template.CreateAsync("texts-traditional", "--samples", "false", "--tests", "false", "--languages", "en,zh-Hant,zh-TW")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Generated_app_with_relations_enums_new_types_and_languages_builds_and_its_migrations_cover_the_model()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var spec = SpecJson.Example();
        spec.Options["registration"] = true;
        spec.Options["colorMode"] = "dark";
        // the infrastructure choices of the other branches together: SQL Server, no Redis, Ollama
        spec.Options["database"] = "sqlServer";
        spec.Options["redis"] = false;
        spec.Options["aiProvider"] = "ollama";
        spec.Options["languages"] = new System.Text.Json.Nodes.JsonArray("en", "de", "fr");
        spec.Modules.Insert(0, new ModuleSpec
        {
            Name = "Crm",
            Entities =
            [
                new EntitySpec
                {
                    Name = "Supplier",
                    Mapping = MappingStrategy.Mapperly,
                    Properties =
                    [
                        new PropertySpec { Name = "Company", Required = true, MaxLength = 120, Display = true, Unique = true },
                        new PropertySpec { Name = "Site", Type = PropertyType.Url },
                        new PropertySpec { Name = "Rating", Type = PropertyType.Int, Default = "3", Facet = true },
                        new PropertySpec { Name = "Opens", Type = PropertyType.TimeOnly },
                        new PropertySpec { Name = "Weight", Type = PropertyType.Double, Default = "1.5" },
                    ],
                },
            ],
        });
        spec.Modules[1].Entities.Add(new EntitySpec
        {
            Name = "Delivery",
            Plural = "Deliveries",
            Mapping = MappingStrategy.Mapperly,
            Properties =
            [
                new PropertySpec { Name = "Order", Type = PropertyType.Reference, Reference = "Order", Required = true, OnDelete = DeleteBehavior.Cascade, Inverse = "Deliveries" },
                new PropertySpec { Name = "Supplier", Type = PropertyType.Reference, Reference = "Crm.Supplier", OnDelete = DeleteBehavior.SetNull, Facet = true },
                new PropertySpec { Name = "Status", Type = PropertyType.Enum, Enum = "OrderStatus" },
                new PropertySpec { Name = "Fee", Type = PropertyType.Money, Precision = 12, Scale = 3 },
            ],
        });
        using var assets = new TempFolder();
        SpecValidator.Validate(spec, assets.Path).ShouldBeEmpty();
        var settings = new NewSettings(spec, assets.Path, Path.Combine(template.Root, "rich-build"), null);

        var app = await template.CreateAsync("rich-build", NewCommand.TemplateArgs(settings)[7..]);
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        (await NewCommand.CompleteAsync(settings, Ct)).ShouldBe(0);

        var solution = Solution.Find(app);
        var invoice = new EntitySpec
        {
            Name = "Invoice",
            Properties = [FeatureOptions.Property("Number:string:required:30:unique"), FeatureOptions.Property("Supplier:ref=Crm.Supplier:required"), FeatureOptions.Property("Kind:enum=InvoiceKind:default=Final:facet")],
        };
        (await EntityCommand.AddAsync(solution, "Sales", invoice, Ct, [FeatureOptions.EnumSpec("InvoiceKind=Draft,Final")])).ShouldBe(0);
        await Dotnet.RunAsync(app, [.. MigrationsCommand.AddArgs(solution, "AddInvoice")[1..]]);

        Read(app, "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldContain("localization.Languages = [\"en\", \"de\", \"fr\"];");
        Read(app, "src/Shop.Sales/Shop.Sales.csproj").ShouldContain("Shop.Crm.csproj");
        var appHost = Read(app, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("options.UseSqlServer();");
        appHost.ShouldContain("options.WithoutRedis();");
        appHost.ShouldContain("builder.AddOllama(\"ollama\")");
        appHost.ShouldContain("options.UseLibreTranslate([\"en\", \"de\", \"fr\", \"de\"]);");
        await Dotnet.RunAsync(app, "build", "Shop.slnx");
        await Dotnet.RunAsync(app, "test", "--project", "tests/Shop.Migrations.Tests", "--no-build", "--filter-class", "Shop.Migrations.Tests.ModelTests");
    }

    private static string Read(string app, string path) => File.ReadAllText(Path.Combine(app, path));
}

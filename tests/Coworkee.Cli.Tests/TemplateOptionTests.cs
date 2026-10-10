using Coworkee.Cli.Commands;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class TemplateOptionTests(TemplateFixture template)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Modules_switch_off_and_on_with_their_packages_wiring_and_app_host_resources()
    {
        var app = await template.CreateAsync("modules", "--samples", "false", "--chat", "false", "--ai", "false", "--backup", "false", "--notifications", "false", "--auditing", "false",
            "--files", "true", "--features", "true", "--social", "true", "--extendedAttributes", "true", "--elasticsearch", "true", "--eventBus", "rabbitMq", "--storage", "azureBlob", "--clientEntities", "true");

        var infrastructure = Read(app, "src/Shop.Infrastructure/Shop.Infrastructure.csproj");
        foreach (var off in new[] { "Chat", "Ai", "Backup", "Notifications", "Auditing" })
        {
            infrastructure.ShouldNotContain($"\"Coworkee.{off}\"");
        }

        foreach (var on in new[] { "Files", "Features", "Social", "ExtendedAttributes", "Search.Elasticsearch", "EventBus.RabbitMq" })
        {
            infrastructure.ShouldContain($"\"Coworkee.{on}\"");
        }

        var database = Read(app, "src/Shop.Infrastructure/ShopDatabaseModule.cs");
        database.ShouldContain("typeof(Coworkee.Files.CoworkeeFilesModule)");
        database.ShouldContain("typeof(Coworkee.EventBus.RabbitMq.CoworkeeRabbitMqEventBusModule)");
        database.ShouldNotContain("Chat");
        database.ShouldNotContain("Auditing");
        Directory.Exists(Path.Combine(app, "src/Shop.Infrastructure/Migrations")).ShouldBeFalse();
        Read(app, "README.md").ShouldContain("coworkee migrations add Initial");
        var appHost = Read(app, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("builder.AddRabbitMQ(\"rabbitmq\")");
        appHost.ShouldContain("AddAzureStorage(\"storage\").RunAsEmulator()");
        Read(app, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldContain("Aspire.Hosting.RabbitMQ");
        Read(app, "src/Shop.Web.Client/Shop.Web.Client.csproj").ShouldContain("Coworkee.Client.Blazor.ClientEntities");
        Read(app, "src/Shop.Web.Client/Program.cs").ShouldContain("builder.Services.AddCoworkeeClientEntities();");
        Read(app, "src/Shop.Web.Client/Navigation/ShopNavigation.cs").ShouldNotContain("/chat");
        Read(app, "src/Shop.Migrations/DemoSeed.cs").ShouldNotContain("ChatPermissions");
        TemplateTests.ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task Samples_without_ai_leave_out_the_ai_tests_and_the_migration_stays_only_with_the_shipped_modules()
    {
        var app = await template.CreateAsync("no-ai", "--ai", "false");

        File.Exists(Path.Combine(app, "tests/Shop.Api.Tests/AiToolTests.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(app, "tests/Shop.Api.Tests/CatalogTests.cs")).ShouldBeTrue();
        Directory.Exists(Path.Combine(app, "src/Shop.Infrastructure/Migrations")).ShouldBeFalse();
        Directory.Exists(Path.Combine(await template.CreateAsync("samples-attributes", "--extendedAttributes", "true"), "src/Shop.Infrastructure/Migrations")).ShouldBeTrue();
        TemplateTests.ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task Branding_reaches_the_theme_the_client_and_the_sign_in_pages()
    {
        var app = await template.CreateAsync("brand", "--title", "Acme Shop", "--theme", "HighContrast", "--primaryColor", "#0a7d5a", "--secondaryColor", "#ff8f00", "--logo", "logo.png",
            "--signInLayout", "Split", "--signInMedia", "/media/welcome.mp4", "--signInMediaSide", "Right");

        var infrastructure = Read(app, "src/Shop.Infrastructure/ShopInfrastructureModule.cs");
        infrastructure.ShouldContain("themes.DefaultTheme = \"Acme Shop\";");
        infrastructure.ShouldContain("new Coworkee.Theming.AppTheme(\"Acme Shop\", \"High Contrast\"");
        infrastructure.ShouldContain("[\"Primary\"] = \"#0a7d5a\",");
        infrastructure.ShouldContain("[\"Secondary\"] = \"#ff8f00\",");
        var appHost = Read(app, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("options.LogoUrl = \"/logo.png\";");
        appHost.ShouldContain("options.SignIn.Layout = \"Split\";");
        appHost.ShouldContain("options.SignIn.MediaSide = Coworkee.Contracts.Theming.SignInMediaSide.Right;");
        appHost.ShouldContain("options.SignIn.MediaUrl = \"/media/welcome.mp4\";");
        Read(app, "src/Shop.Web.Client/Program.cs").ShouldContain("options.AppLogo = \"logo.png\";");
        Read(app, "src/Shop.Web/App.razor").ShouldContain("<link rel=\"icon\" href=\"logo.png\" />");
        File.Exists(Path.Combine(app, "src/Shop.Web.Client/wwwroot/coworkee-icon.svg")).ShouldBeFalse();
        Directory.Exists(Path.Combine(app, "src/Shop.Infrastructure/Migrations")).ShouldBeTrue();
        TemplateTests.ShouldHaveNoLeftovers(app);

        Read(await template.CreateAsync("theme-only", "--theme", "Forest"), "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldContain("themes.DefaultTheme = \"Forest\";");
        var plain = await template.CreateAsync("default-theme");
        Read(plain, "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldNotContain("ThemingOptions");
        Read(plain, "src/Shop.AppHost/AppHost.cs").ShouldNotContain("SignIn");
    }

    [Fact]
    public async Task Generated_modules_wire_into_the_files_of_the_template()
    {
        var app = await template.CreateAsync("features", "--samples", "false");
        var solution = Solution.Find(app);
        var order = new EntitySpec { Name = "Order", ClientEntity = true, Properties = [new PropertySpec { Name = "Number", Required = true, MaxLength = 50 }] };

        await FeatureWriter.AddModuleAsync(solution, "Sales", [new EntityModel("Shop", "Sales", order, MappingStrategy.Mapperly)], Ct);
        await FeatureWriter.AddEntityAsync(solution, new EntityModel("Shop", "Sales", new EntitySpec { Name = "Invoice", Properties = [new PropertySpec { Name = "Total", Type = PropertyType.Decimal }] }, MappingStrategy.Manual), Ct);

        Read(app, "Shop.slnx").ShouldContain("<Project Path=\"src/Shop.Sales/Shop.Sales.csproj\" />");
        Read(app, "src/Shop.Infrastructure/Shop.Infrastructure.csproj").ShouldContain("..\\Shop.Sales\\Shop.Sales.csproj");
        Read(app, "src/Shop.Infrastructure/ShopDatabaseModule.cs").ShouldContain("typeof(Shop.Sales.ShopSalesModule)");
        var program = Read(app, "src/Shop.Web.Client/Program.cs");
        program.ShouldContain("builder.Services.AddSingleton<INavigationContributor, Shop.Web.Client.Navigation.InvoicesNavigation>();");
        program.ShouldContain("using Coworkee.Client.Blazor.ClientEntities;");
        program.IndexOf("AddCoworkeeClientEntities", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("var host = builder.Build();", StringComparison.Ordinal));
        Read(app, "src/Shop.Web.Client/Shop.Web.Client.csproj").ShouldContain("Coworkee.Client.Blazor.ClientEntities");
        Read(app, "Directory.Packages.props").ShouldContain("Riok.Mapperly");
        Read(app, "src/Shop.Sales/ShopSalesModule.cs").ShouldContain("app.MapInvoiceEndpoints();");
        File.Exists(Path.Combine(app, "src/Shop.Web.Client/Pages/Sales/OrdersPage.razor")).ShouldBeTrue();
        TemplateTests.ShouldHaveNoLeftovers(app);
    }

    [Fact]
    public async Task Generated_app_with_options_branding_and_spec_entities_builds_and_its_migration_covers_the_model()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        using var assets = new TempFolder();
        File.WriteAllText(assets.File("brand.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        var spec = new AppSpec
        {
            Name = "Shop",
            Options = { ["samples"] = false, ["chat"] = false, ["files"] = true, ["social"] = true, ["eventBus"] = "rabbitMq", ["storage"] = "azureBlob", ["mapping"] = "manual" },
            Branding = new BrandingSpec { Title = "Acme", Theme = "Midnight", PrimaryColor = "#0a7d5a", Logo = "brand.svg", SignInLayout = "Split", SignInMedia = "https://cdn.test/welcome.mp4" },
            Modules =
            [
                new ModuleSpec
                {
                    Name = "Sales",
                    Entities =
                    [
                        new EntitySpec { Name = "Customer", Properties = [new PropertySpec { Name = "Name", Required = true, MaxLength = 200 }, new PropertySpec { Name = "IsActive", Type = PropertyType.Bool }] },
                        new EntitySpec
                        {
                            Name = "Order",
                            ClientEntity = true,
                            Mapping = MappingStrategy.Mapperly,
                            Properties =
                            [
                                new PropertySpec { Name = "Number", Required = true, MaxLength = 50 },
                                new PropertySpec { Name = "Total", Type = PropertyType.Decimal, Required = true },
                                new PropertySpec { Name = "OrderedAt", Type = PropertyType.DateTime },
                                new PropertySpec { Name = "CustomerId", Type = PropertyType.Guid },
                                new PropertySpec { Name = "Lines", Type = PropertyType.Int },
                                new PropertySpec { Name = "Weight", Type = PropertyType.Double },
                                new PropertySpec { Name = "Code", Type = PropertyType.Long },
                            ],
                        },
                    ],
                },
            ],
        };
        SpecValidator.Validate(spec, assets.Path).ShouldBeEmpty();
        var settings = new NewSettings(spec, assets.Path, Path.Combine(template.Root, "spec-build"), null);

        var app = await template.CreateAsync("spec-build", NewCommand.TemplateArgs(settings)[7..]);
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        (await NewCommand.CompleteAsync(settings, Ct)).ShouldBe(0);

        File.Exists(Path.Combine(app, "src/Shop.Web.Client/wwwroot/brand.svg")).ShouldBeTrue();
        Directory.GetFiles(Path.Combine(app, "src/Shop.Infrastructure/Migrations"), "*_Initial.cs").ShouldHaveSingleItem();
        await Dotnet.RunAsync(app, "build", "Shop.slnx");
        await Dotnet.RunAsync(app, "test", "--project", "tests/Shop.Migrations.Tests", "--no-build", "--filter-class", "Shop.Migrations.Tests.ModelTests");
    }

    private static string Read(string app, string path) => File.ReadAllText(Path.Combine(app, path));
}

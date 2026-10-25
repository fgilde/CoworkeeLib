using Coworkee.Cli.Commands;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class TemplateOptionTests(TemplateFixture template)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Webhooks_bring_their_module_and_let_the_sample_products_send_events()
    {
        var app = await template.CreateAsync("webhooks", "--webhooks", "true");
        var plain = await template.CreateAsync("no-webhooks");

        Read(app, "src/Shop.Infrastructure/Shop.Infrastructure.csproj").ShouldContain("\"Coworkee.Webhooks\"");
        Read(app, "src/Shop.Infrastructure/ShopDatabaseModule.cs").ShouldContain("typeof(Coworkee.Webhooks.CoworkeeWebhooksModule)");
        Read(app, "src/Shop.Catalog/CatalogModule.cs").ShouldContain("webhooks.Entity<Product>()");
        Read(plain, "src/Shop.Catalog/CatalogModule.cs").ShouldNotContain("Webhooks");
        Read(plain, "src/Shop.Infrastructure/Shop.Infrastructure.csproj").ShouldNotContain("Coworkee.Webhooks");
    }

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
    public async Task Workflows_bring_the_module_and_the_product_approval_and_elsa_the_api_with_a_sample_workflow()
    {
        var off = await template.CreateAsync("workflows-off");
        Read(off, "src/Shop.Infrastructure/Shop.Infrastructure.csproj").ShouldNotContain("Coworkee.Workflows");
        Read(off, "src/Shop.Catalog/CatalogModule.cs").ShouldNotContain("AddProductApproval");
        Read(off, "src/Shop.Web.Client/Pages/Catalog/Products.razor").ShouldNotContain("CoworkeeWorkflowColumn");
        File.Exists(Path.Combine(off, "src/Shop.Catalog/Features/Products/ProductApproval.cs")).ShouldBeFalse();
        Directory.Exists(Path.Combine(off, "src/Shop.Api/Workflows")).ShouldBeFalse();

        var on = await template.CreateAsync("workflows-elsa", "--workflows", "true", "--elsa", "true");
        Read(on, "src/Shop.Infrastructure/Shop.Infrastructure.csproj").ShouldContain("\"Coworkee.Workflows\"");
        Read(on, "src/Shop.Infrastructure/ShopDatabaseModule.cs").ShouldContain("typeof(Coworkee.Workflows.CoworkeeWorkflowsModule)");
        Read(on, "src/Shop.Catalog/CatalogModule.cs").ShouldContain("context.Services.AddProductApproval();");
        Read(on, "src/Shop.Web.Client/Pages/Catalog/Products.razor").ShouldContain("<CoworkeeWorkflowColumn T=\"ProductDto\" EntityType=\"Products\"");
        Read(on, "src/Shop.Migrations/DemoSeed.cs").ShouldContain("WorkflowPermissions.View, WorkflowPermissions.Transition");
        Read(on, "src/Shop.Api/Shop.Api.csproj").ShouldContain("\"Coworkee.Workflows.Elsa\"");
        Read(on, "src/Shop.Api/ShopApiModule.cs").ShouldContain("typeof(Coworkee.Workflows.Elsa.CoworkeeWorkflowsElsaModule)");
        Read(on, "src/Shop.Api/ShopApiModule.cs").ShouldContain("AddWorkflow<Workflows.ProductApprovedWorkflow>()");
        Directory.Exists(Path.Combine(on, "src/Shop.Infrastructure/Migrations")).ShouldBeFalse();
        Directory.Exists(Path.Combine(on, "src/Shop.ElsaStudio")).ShouldBeFalse();
        Read(on, "src/Shop.AppHost/AppHost.cs").ShouldNotContain("AddElsaStudio");
        TemplateTests.ShouldHaveNoLeftovers(on);

        var studio = await template.CreateAsync("workflows-elsa-studio", "--workflows", "true", "--elsa", "true", "--elsaStudio", "true");
        Read(studio, "src/Shop.ElsaStudio/Shop.ElsaStudio.csproj").ShouldContain("\"Coworkee.Workflows.Elsa.Studio\"");
        Read(studio, "src/Shop.ElsaStudio/Program.cs").ShouldContain("builder.AddCoworkeeElsaStudio();");
        Read(studio, "src/Shop.AppHost/AppHost.cs").ShouldContain("app.AddElsaStudio<Projects.Shop_ElsaStudio>();");
        Read(studio, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldContain("Shop.ElsaStudio.csproj");
        Read(studio, "Shop.slnx").ShouldContain("src/Shop.ElsaStudio/Shop.ElsaStudio.csproj");
        Read(studio, "Directory.Packages.props").ShouldContain("\"Coworkee.Workflows.Elsa.Studio\"");
        TemplateTests.ShouldHaveNoLeftovers(studio);

        var studioWithoutElsa = await template.CreateAsync("studio-without-elsa", "--workflows", "true", "--elsaStudio", "true");
        Directory.Exists(Path.Combine(studioWithoutElsa, "src/Shop.ElsaStudio")).ShouldBeFalse();

        var builtIn = await template.CreateAsync("workflows-only", "--workflows", "true");
        Read(builtIn, "src/Shop.Api/Shop.Api.csproj").ShouldNotContain("Elsa");
        Directory.Exists(Path.Combine(builtIn, "src/Shop.Api/Workflows")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("sqlServer", "options.UseSqlServer();", "DatabaseProviders.SqlServer")]
    [InlineData("sqlite", "options.UseSqlite();", "DatabaseProviders.Sqlite")]
    public async Task Database_reaches_the_app_host_and_the_infrastructure_without_the_postgres_migration_and_backups(string database, string appHostCall, string provider)
    {
        var app = await template.CreateAsync($"db-{database}", "--database", database);

        Read(app, "src/Shop.AppHost/AppHost.cs").ShouldContain(appHostCall);
        Read(app, "src/Shop.Infrastructure/ShopInfrastructureModule.cs").ShouldContain($"public const string DatabaseProvider = {provider};");
        Directory.Exists(Path.Combine(app, "src/Shop.Infrastructure/Migrations")).ShouldBeFalse();
        Read(app, "src/Shop.Infrastructure/Shop.Infrastructure.csproj").ShouldNotContain("\"Coworkee.Backup\"");
        Read(app, "src/Shop.Web.Client/Navigation/ShopNavigation.cs").ShouldNotContain("/admin/backups");
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
    public async Task Ai_provider_and_redis_reach_the_app_host_and_the_service_defaults()
    {
        var ollama = await template.CreateAsync("ai-ollama", "--samples", "false", "--aiProvider", "ollama", "--redis", "false");
        var appHost = Read(ollama, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("builder.AddOllama(\"ollama\")");
        appHost.ShouldContain("Defaults[\"Ai.Provider\"], \"ollama\")");
        appHost.ShouldContain("options.WithoutRedis();");
        Read(ollama, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldContain("CommunityToolkit.Aspire.Hosting.Ollama");
        var defaults = Read(ollama, "src/Shop.ServiceDefaults/Extensions.cs");
        defaults.ShouldNotContain("AddRedisClient");
        defaults.ShouldContain("if (http.Name != \"Coworkee.Ai\")");
        TemplateTests.ShouldHaveNoLeftovers(ollama);

        var azure = await template.CreateAsync("ai-azure", "--samples", "false", "--aiProvider", "azureOpenAI");
        appHost = Read(azure, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("Defaults[\"Ai.Provider\"], \"azureOpenAI\")");
        appHost.ShouldNotContain("AddOllama");
        appHost.ShouldNotContain("WithoutRedis");
        Read(azure, "src/Shop.ServiceDefaults/Extensions.cs").ShouldContain("builder.AddRedisClient(\"redis\")");
        Read(azure, "src/Shop.ServiceDefaults/Shop.ServiceDefaults.csproj").ShouldContain("Aspire.StackExchange.Redis");
        TemplateTests.ShouldHaveNoLeftovers(azure);

        var router = await template.CreateAsync("ai-router", "--samples", "false", "--aiProvider", "openRouter", "--aiModel", "openai/gpt-5", "--aiEndpoint", "https://proxy.acme.test/v1");
        appHost = Read(router, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("Defaults[\"Ai.Provider\"], \"openRouter\")");
        appHost.ShouldContain("string aiModel = \"openai/gpt-5\", aiEndpoint = \"https://proxy.acme.test/v1\";");
        appHost.ShouldContain("if (aiParameters.ContainsKey(\"ApiKey\"))");
        TemplateTests.ShouldHaveNoLeftovers(router);

        var lmStudio = await template.CreateAsync("ai-lmstudio", "--samples", "false", "--aiProvider", "lmStudio");
        appHost = Read(lmStudio, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("string aiModel = \"\", aiEndpoint = \"\";");
        appHost.ShouldContain("LM Studio serves the model");
        appHost.ShouldNotContain("AddOllama");

        var none = await template.CreateAsync("ai-none", "--samples", "false", "--ai", "false", "--aiProvider", "ollama");
        Read(none, "src/Shop.AppHost/AppHost.cs").ShouldNotContain("Ai.Provider");
        Read(none, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldNotContain("Ollama");
        Read(none, "src/Shop.ServiceDefaults/Extensions.cs").ShouldNotContain("Coworkee.Ai");
    }

    [Fact]
    public async Task Grafana_and_webdatastudio_are_on_by_default_and_can_be_left_out()
    {
        var on = await template.CreateAsync("tools-on", "--samples", "false");
        var appHost = Read(on, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("options.UseGrafana();");
        appHost.ShouldContain("options.UseWebDataStudio();");
        appHost.ShouldContain("options.Administrator = new(administrator.Email, administrator.Password);");
        appHost.ShouldContain("using Shop.Migrations;");
        Read(on, "src/Shop.ServiceDefaults/Extensions.cs").ShouldContain("GRAFANA_OTLP_ENDPOINT");

        var off = await template.CreateAsync("tools-off", "--samples", "false", "--grafana", "false", "--webDataStudio", "false");
        appHost = Read(off, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldNotContain("UseGrafana");
        appHost.ShouldNotContain("UseWebDataStudio");
        appHost.ShouldNotContain("Administrator");
        appHost.ShouldNotContain("using Shop.Migrations;");
        Read(off, "LOGIN.md").ShouldNotContain("grafana");
        TemplateTests.ShouldHaveNoLeftovers(off);
    }

    [Fact]
    public async Task Kibana_comes_with_the_search_only()
    {
        var on = await template.CreateAsync("kibana-on", "--samples", "false", "--elasticsearch", "true", "--kibana", "true");
        Read(on, "src/Shop.AppHost/AppHost.cs").ShouldContain("options.UseKibana();");
        Read(on, "LOGIN.md").ShouldContain("*kibana*");
        SignInInfo.Read(on)!.Tools.ShouldContain("Kibana");

        var alone = await template.CreateAsync("kibana-alone", "--samples", "false", "--kibana", "true");
        Read(alone, "src/Shop.AppHost/AppHost.cs").ShouldNotContain("UseKibana");
        TemplateTests.ShouldHaveNoLeftovers(alone);
    }

    [Fact]
    public async Task AspireUI_is_off_by_default_and_adds_the_host_the_package_and_the_sign_on_with_keycloak()
    {
        var off = await template.CreateAsync("aspireui-off", "--samples", "false", "--keycloak", "true");
        Read(off, "src/Shop.AppHost/AppHost.cs").ShouldNotContain("AddAspireUI");
        Read(off, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldNotContain("AspireUI");
        Read(off, "LOGIN.md").ShouldNotContain("aspireui");
        TemplateTests.ShouldHaveNoLeftovers(off);

        var plain = await template.CreateAsync("aspireui-plain", "--samples", "false", "--aspireUI", "true", "--grafana", "false", "--webDataStudio", "false");
        var appHost = Read(plain, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain("using Nextended.Aspire.Hosting.AspireUI;");
        appHost.ShouldContain("var administrator = DemoUsers.All.First(u => u.IsAdmin);");
        appHost.ShouldContain("if (app.Deploys(CoworkeeTools.AspireUI))");
        appHost.ShouldContain("builder.AddAspireUI().WithCoworkee(app).WithRetrieveApi();");
        appHost.ShouldContain("options.Administrator = new(administrator.Email, administrator.Password);");
        appHost.ShouldContain("aspireUi.WithSeedFromDirectory(builder.AppHostDirectory);");
        Read(plain, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldContain("<PackageReference Include=\"Nextended.Aspire.Hosting.AspireUI\" />");
        Read(plain, "Directory.Packages.props").ShouldContain("Nextended.Aspire.Hosting.AspireUI");
        Read(plain, "LOGIN.md").ShouldContain("*aspireui*");
        SignInInfo.Read(plain)!.Tools.ShouldBe(["AspireUI"]);
        TemplateTests.ShouldHaveNoLeftovers(plain);

        var keycloak = await template.CreateAsync("aspireui-keycloak", "--samples", "false", "--aspireUI", "true", "--keycloak", "true");
        appHost = Read(keycloak, "src/Shop.AppHost/AppHost.cs");
        appHost.ShouldContain(".WithCoworkee(app)");
        appHost.ShouldContain("options.UseGrafana();");
        SignInInfo.Read(keycloak)!.Tools.ShouldBe(["Grafana", "WebDataStudio", "AspireUI"]);
        TemplateTests.ShouldHaveNoLeftovers(keycloak);
    }

    [Fact]
    public async Task AirWorks_is_off_by_default_and_adds_the_client_project_its_app_host_line_and_the_about_credit()
    {
        var off = await template.CreateAsync("airworks-off", "--samples", "false");
        Directory.Exists(Path.Combine(off, "src", "Shop.AirWorks")).ShouldBeFalse();
        Read(off, "Shop.slnx").ShouldNotContain("AirWorks");
        Read(off, "src/Shop.AppHost/AppHost.cs").ShouldNotContain("AirWorks");
        Read(off, "src/Shop.Web.Client/Program.cs").ShouldNotContain("AirWorks");
        TemplateTests.ShouldHaveNoLeftovers(off);

        var on = await template.CreateAsync("airworks-on", "--airworks", "true", "--logo", "brand.png");
        Read(on, "Shop.slnx").ShouldContain("src/Shop.AirWorks/Shop.AirWorks.csproj");
        Read(on, "src/Shop.AppHost/AppHost.cs").ShouldContain("app.AddAirWorks<Projects.Shop_AirWorks>();");
        Read(on, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldContain("..\\Shop.AirWorks\\Shop.AirWorks.csproj");
        Read(on, "src/Shop.Web.Client/Program.cs").ShouldContain("AboutCredit.AirWorks(");
        Read(on, "src/Shop.AirWorks/Shop.AirWorks.csproj").ShouldContain("<CoworkeePackage Include=\"Coworkee.Bff\" />");
        var main = Read(on, "src/Shop.AirWorks/ClientApp/src/main.ts");
        main.ShouldContain("'../../../Shop.Web.Client/wwwroot/brand.png?url'");
        main.ShouldContain("entityApp({ entitySet: 'Products'");
        main.ShouldNotContain("#if");
        var package = Read(on, "src/Shop.AirWorks/ClientApp/package.json");
        package.ShouldContain("\"name\": \"shop-airworks\"");
        package.ShouldContain("file:vendor/coworkee-airworks.tgz");
        File.Exists(Path.Combine(on, "src", "Shop.AirWorks", "ClientApp", "vendor", "coworkee-client.tgz")).ShouldBeTrue();
        Read(on, "src/Shop.AirWorks/ClientApp/.npmrc").ShouldContain("allow-git=root");
        Read(on, "LOGIN.md").ShouldContain("*shop-airworks*");
        Read(on, "src/Shop.AirWorks/appsettings.json").ShouldContain("\"Launcher\": \"menu\"");
        TemplateTests.ShouldHaveNoLeftovers(on);

        var npm = await template.CreateAsync("airworks-npm", "--airworks", "true", "--airworksPackages", "npm", "--airworksLauncher", "overlay");
        Read(npm, "src/Shop.AirWorks/appsettings.json").ShouldContain("\"Launcher\": \"overlay\"");
        Read(npm, "src/Shop.AirWorks/ClientApp/package.json").ShouldContain($"\"@coworkee/airworks\": \"{template.Version}\"");
        Directory.Exists(Path.Combine(npm, "src", "Shop.AirWorks", "ClientApp", "vendor")).ShouldBeFalse();
        TemplateTests.ShouldHaveNoLeftovers(npm);

        var plain = await template.CreateAsync("airworks-plain", "--airworks", "true", "--samples", "false");
        Read(plain, "src/Shop.AirWorks/ClientApp/src/main.ts").ShouldNotContain("entityApp({");
    }

    [Fact]
    public async Task Generated_app_with_airworks_builds_its_client()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("build-airworks", "--airworks", "true", "--keycloak", "true", "--grafana", "false", "--webDataStudio", "false");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");

        await Dotnet.RunAsync(app, "build", "Shop.slnx");

        File.ReadAllText(Path.Combine(app, "src", "Shop.AirWorks", "wwwroot", "index.html")).ShouldContain("<title>Shop</title>");
        Directory.GetFiles(Path.Combine(app, "src", "Shop.AirWorks", "wwwroot", "icons"), "logo-air.png").ShouldNotBeEmpty();
        File.Exists(Path.Combine(app, "src", "Shop.AirWorks", "wwwroot", "mudex", "mudex.js")).ShouldBeTrue();
        File.Exists(Path.Combine(app, "src", "Shop.AirWorks", "wwwroot", "mudex", "coworkee-frame.html")).ShouldBeTrue();
    }

    [Fact]
    public async Task Generated_app_without_grafana_and_webdatastudio_builds()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("build-no-tools", "--samples", "false", "--grafana", "false", "--webDataStudio", "false", "--keycloak", "true", "--aspireUI", "true");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");

        await Dotnet.RunAsync(app, "build", "Shop.slnx");
    }

    [Fact]
    public async Task Generated_app_with_workflows_elsa_and_the_samples_builds()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("build-workflows", "--workflows", "true", "--elsa", "true", "--elsaStudio", "true", "--grafana", "false", "--webDataStudio", "false");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");

        await Dotnet.RunAsync(app, "build", "Shop.slnx");
    }

    [Fact]
    public async Task Generated_app_with_ollama_and_without_redis_builds()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var app = await template.CreateAsync("build-ollama", "--samples", "false", "--aiProvider", "ollama", "--redis", "false");
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");

        await Dotnet.RunAsync(app, "build", "Shop.slnx");
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

        await FeatureWriter.AddModuleAsync(solution, "Sales", [new EntityModel("Shop", "Sales", order, MappingStrategy.Mapperly)], [], Ct);
        await FeatureWriter.AddEntityAsync(solution, new EntityModel("Shop", "Sales", new EntitySpec { Name = "Invoice", Properties = [new PropertySpec { Name = "Total", Type = PropertyType.Decimal }] }, MappingStrategy.Manual), [], Ct);

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

        // the api starts in its tests without a RabbitMQ broker, and the web host serves the scoped CSS bundle App.razor links
        await Dotnet.RunAsync(app, "test", "--project", "tests/Shop.Api.Tests", "--no-build", "--filter-class", "Shop.Api.Tests.SetupFlowTests");
        Read(app, "src/Shop.Web/App.razor").ShouldContain("@Assets[\"Shop.Web.styles.css\"]");
        var endpoints = Directory.GetFiles(Path.Combine(app, "src/Shop.Web/bin"), "Shop.Web.staticwebassets.endpoints.json", SearchOption.AllDirectories).ShouldHaveSingleItem();
        File.ReadAllText(endpoints).ShouldContain("\"Shop.Web.styles.css\"");
    }

    private static string Read(string app, string path) => File.ReadAllText(Path.Combine(app, path));
}

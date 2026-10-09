using Coworkee.Cli;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli.Tests;

public sealed class CommandTests
{
    [Fact]
    public void New_defaults_to_samples_and_tests_in_a_folder_named_like_the_app()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse("new Shop"));

        settings.ShouldBe(new NewSettings("Shop", "Shop", Samples: true, Keycloak: false, Tests: true, Title: null, TemplateSource: null));
        NewCommand.TemplateArgs(settings).ShouldBe(["dotnet", "new", "coworkee", "-n", "Shop", "-o", "Shop", "--samples", "true", "--keycloak", "false", "--tests", "true"]);
        NewCommand.InstallArgs(settings, "1.2.3").ShouldBe(["dotnet", "new", "install", "Coworkee.Templates@1.2.3"]);
    }

    [Fact]
    public void New_passes_the_options_to_the_template()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse(["new", "Shop", "-o", "apps/shop", "--no-samples", "--keycloak", "--no-tests", "--title", "My Shop", "--template-source", "t.nupkg"]));

        NewCommand.TemplateArgs(settings).ShouldBe(
            ["dotnet", "new", "coworkee", "-n", "Shop", "-o", "apps/shop", "--samples", "false", "--keycloak", "true", "--tests", "false", "--title", "My Shop"]);
        NewCommand.InstallArgs(settings, "1.2.3").ShouldBe(["dotnet", "new", "install", Path.GetFullPath("t.nupkg"), "--force"]);
        NewCommand.Read(CliApp.Build().Parse("new Shop --samples false")).Samples.ShouldBeFalse();
    }

    [Theory]
    [InlineData("new my.app")]
    [InlineData("new 1App")]
    [InlineData("module add Order-Lines")]
    [InlineData("migrations add")]
    public void Invalid_input_is_rejected(string args) => CliApp.Build().Parse(args).Errors.ShouldNotBeEmpty();

    [Fact]
    public void Run_and_migrations_target_the_projects_of_the_solution()
    {
        using var folder = new TempFolder();
        var appHost = folder.File("src/Shop.AppHost/Shop.AppHost.csproj");
        var infrastructure = folder.File("src/Shop.Infrastructure/Shop.Infrastructure.csproj");
        folder.File("Shop.slnx");

        var solution = Solution.Find(Path.Combine(folder.Path, "src", "Shop.AppHost"));

        solution.Name.ShouldBe("Shop");
        RunCommand.Args(solution).ShouldBe(["dotnet", "run", "--project", appHost]);
        MigrationsCommand.AddArgs(solution, "AddOrders")
            .ShouldBe(["dotnet", "ef", "migrations", "add", "AddOrders", "--project", infrastructure, "--startup-project", infrastructure]);
    }

    [Fact]
    public void Update_picks_the_newest_version_and_rewrites_the_props()
    {
        UpdateCommand.Latest(["1.0.0", "1.1.0", "1.2.0-preview.1"], prerelease: false).ShouldBe("1.1.0");
        UpdateCommand.Latest(["1.0.0", "1.1.0", "1.2.0-preview.1"], prerelease: true).ShouldBe("1.2.0-preview.1");
        UpdateCommand.SetVersion("<Project><CoworkeeVersion>1.0.0</CoworkeeVersion></Project>", "1.1.0")
            .ShouldBe("<Project><CoworkeeVersion>1.1.0</CoworkeeVersion></Project>");
        Should.Throw<CliException>(() => UpdateCommand.SetVersion("<Project />", "1.1.0"));
    }

    [Fact]
    public void Doctor_compares_the_sdk_with_global_json()
    {
        DoctorCommand.RequiredSdk("""{ "sdk": { "version": "10.0.300" } }""").ShouldBe("10.0.300");
        DoctorCommand.RequiredSdk(null).ShouldBe("10.0.100");
        DoctorCommand.SdkSatisfies("10.0.301", "10.0.300").ShouldBeTrue();
        DoctorCommand.SdkSatisfies("11.0.100-preview.1", "10.0.300").ShouldBeTrue();
        DoctorCommand.SdkSatisfies("9.0.318", "10.0.300").ShouldBeFalse();
        DoctorCommand.SdkSatisfies(string.Empty, "10.0.300").ShouldBeFalse();
    }

    [Fact]
    public void Module_scaffold_replaces_every_token_and_registers_the_module()
    {
        ModuleCommand.EntityName("Orders", null).ShouldBe("Order");
        ModuleCommand.EntityName("Inventory", null).ShouldBe("InventoryItem");
        ModuleCommand.EntityName("Orders", "Purchase").ShouldBe("Purchase");

        var files = ModuleCommand.Files(ModuleCommand.Tokens("Shop", "Orders", "Order")).ToList();

        files.Select(f => f.Path).ShouldContain("src/Shop.Orders/ShopOrdersModule.cs");
        files.Select(f => f.Path).ShouldContain("src/Shop.Contracts/Orders/OrderDto.cs");
        files.ShouldAllBe(f => !f.Path.Contains("__") && !f.Content.Contains("__"));
        ModuleCommand.Register("[DependsOn(typeof(A))]\npublic sealed class ShopDatabaseModule;", "Shop.Orders.ShopOrdersModule")
            .ShouldStartWith("[DependsOn(typeof(Shop.Orders.ShopOrdersModule), typeof(A))]");
    }
}

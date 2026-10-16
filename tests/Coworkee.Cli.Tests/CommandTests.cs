using Coworkee.Cli;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli.Tests;

public sealed class CommandTests
{
    [Fact]
    public void New_defaults_to_samples_and_tests_in_a_folder_named_like_the_app()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse("new Shop"));

        settings.Spec.Name.ShouldBe("Shop");
        settings.Output.ShouldBe(Path.GetFullPath("Shop"));
        NewCommand.TemplateArgs(settings).ShouldBe(["dotnet", "new", "coworkee", "-n", "Shop", "-o", Path.GetFullPath("Shop")]);
        NewCommand.InstallArgs(settings, "1.2.3").ShouldBe(["dotnet", "new", "install", "Coworkee.Templates@1.2.3"]);
    }

    [Fact]
    public void New_passes_the_options_to_the_template()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse(["new", "Shop", "-o", "apps/shop", "--no-samples", "--keycloak", "--no-tests", "--title", "My Shop", "--template-source", "t.nupkg"]));

        NewCommand.TemplateArgs(settings).ShouldBe(
            ["dotnet", "new", "coworkee", "-n", "Shop", "-o", Path.GetFullPath("apps/shop"), "--title", "My Shop", "--samples", "false", "--tests", "false", "--keycloak", "true"]);
        NewCommand.InstallArgs(settings, "1.2.3").ShouldBe(["dotnet", "new", "install", Path.GetFullPath("t.nupkg"), "--force"]);
        NewCommand.Read(CliApp.Build().Parse("new Shop --samples false")).Spec.Options["samples"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void The_AirWorks_client_takes_the_bundled_packages_unless_npm_is_chosen()
    {
        NewCommand.TemplateArgs(NewCommand.Read(CliApp.Build().Parse("new Shop --airworks"))).ShouldNotContain("--airworksPackages");
        NewCommand.TemplateArgs(NewCommand.Read(CliApp.Build().Parse("new Shop --airworks --airworks-packages npm")))[^2..].ShouldBe(["--airworksPackages", "npm"]);
    }

    [Theory]
    [InlineData("new my.app")]
    [InlineData("new 1App")]
    [InlineData("module add Order-Lines")]
    [InlineData("entity add Sales 1Order")]
    [InlineData("migrations add")]
    [InlineData("new Shop --theme Pink")]
    [InlineData("new Shop --event-bus kafka")]
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
    public void Doctor_reads_the_versions_and_ports_of_the_app()
    {
        DoctorCommand.AspireSdk("""<Project Sdk="Aspire.AppHost.Sdk/13.6.1">""").ShouldBe("13.6.1");
        DoctorCommand.EfRuntime("""<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.13" />""").ShouldBe("10.0.13");
        DoctorCommand.ToolVersion("Entity Framework Core .NET Command-line Tools\n10.0.13\n").ShouldBe("10.0.13");
        DoctorCommand.ToolVersion("13.1.0+4f7a2c").ShouldBe("13.1.0");
        DoctorCommand.Ports("""{ "profiles": { "https": { "applicationUrl": "https://localhost:7300;http://localhost:5300", "environmentVariables": { "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21000" } } } }""")
            .ShouldBe([7300, 5300, 21000]);
    }

    [Theory]
    [InlineData("13.1.0", "13.6.1", 2, -1)]
    [InlineData("13.6.0", "13.6.1", 2, 0)]
    [InlineData("10.0.12", "10.0.13", 3, -1)]
    [InlineData("0.1.0-dev.12", "0.1.0-*", 3, 0)]
    [InlineData("0.0.1.26", "0.0.1.25", 4, 1)]
    public void Doctor_compares_versions(string a, string b, int parts, int expected) =>
        DoctorCommand.Compare(a, b, parts).ShouldBe(expected);
}

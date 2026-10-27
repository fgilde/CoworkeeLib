using System.Text.RegularExpressions;
using Coworkee.Cli.Apps;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli.Tests;

public sealed class UpgradeTests
{
    private const string Props = """
        <Project>
          <PropertyGroup>
            <CoworkeeVersion>0.0.1.14</CoworkeeVersion>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
            <PackageVersion Include="Aspire.Hosting.Testing" Version="13.7.0" />
          </ItemGroup>
        </Project>
        """;

    private static string Old(TempFolder folder)
    {
        void Put(string path, string text) => File.WriteAllText(folder.File(path), text);
        Put("Directory.Packages.props", Props);
        Put("src/Shop.AppHost/Shop.AppHost.csproj", """<Project Sdk="Aspire.AppHost.Sdk/13.6.0"></Project>""");
        Put("src/Shop.Web/App.razor", "<html>\n<body>\n    <Routes />\n</body>\n</html>\n");
        Put("src/Shop.Web/Program.cs", "var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();\n");
        Put("src/Shop.Catalog/ThemeSeed.cs", "theme.LogoSvg = svg;\n");
        Put("src/Shop.Catalog/Mail.cs", "class Fake : ISmtpTransport { }\n");
        Put("tests/Shop.Api.Tests/ApiFixture.cs", "public sealed class ApiFixture : PostgresFixture { }\n");
        Put("src/Shop.Web/obj/Copy.cs", "PostgresFixture LogoSvg\n");
        return folder.Path;
    }

    [Fact]
    public void Rules_are_ordered_and_between_takes_the_versions_after_the_old_one()
    {
        UpgradeRules.All.Select(r => r.Version).ShouldBe(UpgradeRules.All.Select(r => r.Version).OrderBy(v => Version.Parse(v)));
        UpgradeRules.Between("0.0.1.21", "0.0.1.23").Select(r => r.Version).ShouldBe(["0.0.1.22", "0.0.1.23"]);
        UpgradeRules.Between(UpgradeRules.All[^1].Version, "0.1.0-dev.20261015184054").Select(r => r.Version).ShouldBeEmpty();
        UpgradeRules.Between(null, "0.0.1.15").Select(r => r.Version).ShouldBe(["0.0.1.15"]);
        UpgradeRules.All.ShouldAllBe(r => r.Breaking != null && r.Breaking.Length > 0);
        UpgradeRules.All.SelectMany(r => (r.Renames ?? []).Select(x => x.Find).Concat((r.Insertions ?? []).Select(i => i.InsertAfter)).Concat((r.Manual ?? []).Select(m => m.Find))).ShouldAllBe(p => new Regex(p) != null);
    }

    [Fact]
    public void The_odata_rules_name_the_options_without_dollar_and_the_aliases()
    {
        var breaking = UpgradeRules.All.Single(r => r.Version == "0.0.1.42").Breaking!;

        breaking.ShouldContain(b => b.Contains("filter=") && b.Contains("aliases"));
    }

    [Fact]
    public void Every_version_with_breaking_changes_in_the_changelog_has_rules()
    {
        var changelog = File.ReadAllText(Path.Combine(Dotnet.RepositoryRoot(), "website", "docs", "reference", "changelog.md")).ReplaceLineEndings("\n");
        var breaking = Regex.Matches(changelog, @"^## (\d+(?:\.\d+){3})\b.*?(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
            .Where(m => m.Value.Contains("### Breaking changes")).Select(m => m.Groups[1].Value);

        breaking.ShouldBeSubsetOf(UpgradeRules.All.Select(r => r.Version));
    }

    [Fact]
    public void Update_renames_bumps_older_packages_only_inserts_once_and_lists_manual_steps()
    {
        using var folder = new TempFolder();
        var root = Old(folder);

        UpdateCommand.Upgrade(root, "0.0.1.25", dryRun: false).ShouldBe(0);

        Read(root, "Directory.Packages.props").ShouldContain("<CoworkeeVersion>0.0.1.25</CoworkeeVersion>");
        Read(root, "Directory.Packages.props").ShouldContain("""Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.13" """.TrimEnd());
        Read(root, "Directory.Packages.props").ShouldContain("""Include="Aspire.Hosting.Testing" Version="13.7.0" """.TrimEnd());
        Read(root, "src/Shop.AppHost/Shop.AppHost.csproj").ShouldContain("Aspire.AppHost.Sdk/13.6.1");
        Read(root, "tests/Shop.Api.Tests/ApiFixture.cs").ShouldContain(": DatabaseFixture");
        Read(root, "src/Shop.Catalog/ThemeSeed.cs").ShouldBe("theme.Logo = svg;\n");
        Read(root, "src/Shop.Web/obj/Copy.cs").ShouldBe("PostgresFixture LogoSvg\n");
        Read(root, "src/Shop.Web/App.razor").ShouldBe("<html>\n<body>\n    <Coworkee.Bff.CoworkeeLoadingScreen Logo=\"coworkee-icon.svg\" />\n    <Routes />\n</body>\n</html>\n");

        UpgradeRules.Apply(root, UpgradeRules.Between("0.0.1.14", "0.0.1.25"), dryRun: false).ShouldBeEmpty();
        var manual = UpgradeRules.All.SelectMany(r => r.Manual ?? []).SelectMany(m => UpgradeRules.Manual(root, m)).Select(h => h.ToString()).ToList();
        manual.ShouldContain("src/Shop.Catalog/Mail.cs:1: class Fake : ISmtpTransport { }");
        manual.ShouldContain("src/Shop.Web/Program.cs:2: var app = builder.Build();");
    }

    [Fact]
    public void A_dry_run_changes_nothing()
    {
        using var folder = new TempFolder();
        var root = Old(folder);

        UpgradeRules.Apply(root, UpgradeRules.Between("0.0.1.14", "0.0.1.25"), dryRun: true).Select(c => c.File).ShouldContain("tests/Shop.Api.Tests/ApiFixture.cs");
        UpdateCommand.Upgrade(root, "0.0.1.25", dryRun: true).ShouldBe(0);

        Read(root, "Directory.Packages.props").ShouldBe(Props);
        Read(root, "tests/Shop.Api.Tests/ApiFixture.cs").ShouldContain("PostgresFixture");
    }

    [Fact]
    public void Update_inserts_a_line_after_the_match_with_its_indentation_and_line_ending_once()
    {
        using var folder = new TempFolder();
        const string program = "using X;\r\n\r\n    var builder = Host.CreateApplicationBuilder(args);\r\nbuilder.AddServiceDefaults();\r\n";
        File.WriteAllText(folder.File("src/Shop.Migrations/Program.cs"), program);
        var rules = UpgradeRules.Between("0.0.1.31", "0.0.1.32");

        UpgradeRules.Apply(folder.Path, rules, dryRun: true).Select(c => c.File).ShouldBe(["src/Shop.Migrations/Program.cs"]);
        Read(folder.Path, "src/Shop.Migrations/Program.cs").ShouldBe(program);

        UpgradeRules.Apply(folder.Path, rules, dryRun: false);
        Read(folder.Path, "src/Shop.Migrations/Program.cs").ShouldBe(
            "using X;\r\n\r\n    var builder = Host.CreateApplicationBuilder(args);\r\n    builder.ConfigureContainer(new DefaultServiceProviderFactory());\r\nbuilder.AddServiceDefaults();\r\n");
        UpgradeRules.Apply(folder.Path, rules, dryRun: false).ShouldBeEmpty();
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));
}

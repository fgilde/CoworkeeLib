using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class UiTests
{
    private const string ToolList = """
        Package Id           Version                     Commands
        -------------------------------------------------------------
        coworkee.cli         1.2.0                       coworkee
        coworkee.studio      1.1.0-preview.2             coworkee-ui
        dotnet-ef            10.0.5                      dotnet-ef
        """;

    [Fact]
    public void Finds_the_installed_studio_version()
    {
        UiCommand.Installed(ToolList).ShouldBe("1.1.0-preview.2");
        UiCommand.Installed("Package Id   Version   Commands\n----\n").ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "1.2.0", "Install")]
    [InlineData("1.1.0", "1.2.0", "Update")]
    [InlineData("1.2.0-preview.9", "1.2.0", "Update")]
    [InlineData("0.1.0-dev.20261001000000", "0.1.0-dev.20261011000000", "Update")]
    [InlineData("1.2.0", "1.2.0", "None")]
    [InlineData("1.3.0", "1.2.0", "None")]
    [InlineData("1.2.0+abc", "1.2.0", "None")]
    public void Installs_or_updates_only_when_missing_or_older(string? installed, string wanted, string step) =>
        UiCommand.Plan(installed, wanted).ToString().ShouldBe(step);

    [Fact]
    public void An_exact_studio_version_below_the_installed_one_is_installed_with_a_downgrade()
    {
        UiCommand.Plan("1.3.0", "1.2.0", exact: true).ToString().ShouldBe("Downgrade");
        UiCommand.Plan("1.2.0", "1.2.0", exact: true).ToString().ShouldBe("None");
        UiCommand.Plan("1.1.0", "1.2.0", exact: true).ToString().ShouldBe("Update");
        UiCommand.ToolArgs(UiCommand.Plan("1.3.0", "1.2.0", exact: true), "1.2.0", prerelease: false, source: null)
            .ShouldBe(["dotnet", "tool", "update", "-g", "Coworkee.Studio", "--version", "1.2.0", "--allow-downgrade"]);
    }

    [Fact]
    public void Prerelease_labels_compare_part_by_part()
    {
        UiCommand.Compare("1.0.0-preview.10", "1.0.0-preview.9").ShouldBeGreaterThan(0);
        UiCommand.Compare("1.0.0-alpha", "1.0.0-alpha.1").ShouldBeLessThan(0);
        UiCommand.Compare("1.0.0", "1.0.0-rc.1").ShouldBeGreaterThan(0);
        UiCommand.Compare("2.0.0", "10.0.0").ShouldBeLessThan(0);
    }

    [Fact]
    public void Installs_and_launches_with_the_right_arguments()
    {
        UiCommand.ToolArgs(UiCommand.Step.Install, "1.2.0", prerelease: false, source: null)
            .ShouldBe(["dotnet", "tool", "install", "-g", "Coworkee.Studio", "--version", "1.2.0"]);
        UiCommand.ToolArgs(UiCommand.Step.Update, "1.2.0-dev.1", prerelease: true, source: "feed")
            .ShouldBe(["dotnet", "tool", "update", "-g", "Coworkee.Studio", "--prerelease", "--add-source", Path.GetFullPath("feed")]);
        UiCommand.LaunchArgs("coworkee-ui", ["--spec", "app.json"]).ShouldBe(["coworkee-ui", "--spec", "app.json"]);
    }

    [Fact]
    public void Launches_from_the_tools_folder_when_the_tool_is_there()
    {
        using var folder = new TempFolder();
        UiCommand.Launcher(folder.Path).ShouldBe("coworkee-ui");

        var tool = folder.File(OperatingSystem.IsWindows() ? "coworkee-ui.exe" : "coworkee-ui");

        UiCommand.Launcher(folder.Path).ShouldBe(tool);
    }

    [Fact]
    public void Unknown_arguments_go_to_the_studio()
    {
        var result = CliApp.Build().Parse(["ui", "--prerelease", "--spec", "app.json", "C:/work"]);

        result.Errors.ShouldBeEmpty();
        result.UnmatchedTokens.ShouldBe(["--spec", "app.json", "C:/work"]);
    }
    [Theory]
    [InlineData("1.0.0", false, "1.1.0")]
    [InlineData("1.0.0", true, "1.2.0-preview.2")]
    [InlineData("1.2.0-preview.1", false, "1.2.0-preview.2")]
    [InlineData("1.1.0", false, null)]
    [InlineData("2.0.0", true, null)]
    public void Finds_a_newer_tool_version_with_prereleases_only_for_prereleases(string current, bool prerelease, string? newer) =>
        ToolUpdates.Newer(["1.0.0", "1.2.0-preview.2", "1.1.0", "1.2.0-preview.1"], current, prerelease).ShouldBe(newer);

    [Fact]
    public void Self_update_updates_the_cli_and_an_installed_studio_to_the_same_version()
    {
        SelfUpdateCommand.Commands("1.3.0", studioInstalled: true).ShouldBe([
            ["dotnet", "tool", "update", "-g", "Coworkee.Cli", "--version", "1.3.0"],
            ["dotnet", "tool", "update", "-g", "Coworkee.Studio", "--version", "1.3.0"]]);
        SelfUpdateCommand.Commands("1.3.0", studioInstalled: false).ShouldHaveSingleItem()[4].ShouldBe("Coworkee.Cli");
        CliApp.Build().Parse(["self-update", "--version", "1.3.0", "--prerelease"]).Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_update_helper_waits_for_the_process_then_updates_and_relaunches()
    {
        var helper = ToolUpdates.Helper(4242, [ToolUpdates.UpdateArgs("Coworkee.Studio", "1.3.0")], ["/tools/coworkee-ui", "--spec", "it's.json"]);
        var script = helper.ArgumentList[^1];

        script.ShouldContain("4242");
        script.ShouldContain("Coworkee.Studio");
        script.IndexOf("4242", StringComparison.Ordinal).ShouldBeLessThan(script.IndexOf("Coworkee.Studio", StringComparison.Ordinal));
        script.IndexOf("Coworkee.Studio", StringComparison.Ordinal).ShouldBeLessThan(script.IndexOf("coworkee-ui", StringComparison.Ordinal));
        script.ShouldContain(OperatingSystem.IsWindows() ? "'it''s.json'" : "'it'\\''s.json'");
    }
}

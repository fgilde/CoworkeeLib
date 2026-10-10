using System.Diagnostics;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class CliTests
{
    [Theory]
    [InlineData("==> Restoring packages", CliLineKind.Step, "Restoring packages")]
    [InlineData("> dotnet restore", CliLineKind.Command, "dotnet restore")]
    [InlineData("warn  The migration could not be added.", CliLineKind.Warning, "The migration could not be added.")]
    [InlineData("Next steps:", CliLineKind.NextSteps, "Next steps:")]
    [InlineData("  Restore complete", CliLineKind.Text, "  Restore complete")]
    public void Reads_the_lines_of_the_cli(string line, CliLineKind kind, string text) =>
        CliLine.Parse(line).ShouldBe(new CliLine(kind, text));

    [Fact]
    public void Lines_on_stderr_are_errors() => CliLine.Parse("==> x", error: true).Kind.ShouldBe(CliLineKind.Error);

    [Fact]
    public void Progress_follows_the_steps_and_collects_warnings_and_next_steps()
    {
        using var folder = new TempFolder();
        var generate = Studio.Create(folder).Generate;
        generate.RunState = RunState.Running;

        foreach (var line in new[] { "==> Installing the template", "> dotnet new install", "==> Restoring packages", "warn  The migration could not be added.", "", "Next steps:", "  cd Shop", "  coworkee run" })
        {
            generate.Add(CliLine.Parse(line));
        }

        generate.Progress.Select(p => (p.Text, p.State)).ShouldBe([("Installing the template", ProgressState.Done), ("Restoring packages", ProgressState.Running)]);
        generate.Warnings.ShouldBe(["The migration could not be added."]);
        generate.NextSteps.ShouldBe(["cd Shop", "coworkee run"]);

        generate.Finish(GenerationOutcome.Failed);
        generate.Progress[1].State.ShouldBe(ProgressState.Failed);
        generate.IsFailed.ShouldBeTrue();
        generate.ShowLog.ShouldBeTrue();
    }

    [Fact]
    public async Task Validates_then_creates_with_paths_on_the_command_line()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var logo = elsewhere.File("brand.svg");
        File.WriteAllText(logo, "<svg/>");
        var calls = new List<ProcessStartInfo>();
        string? writtenSpec = null;
        var main = Studio.Create(folder, (start, output, _) =>
        {
            calls.Add(start);
            writtenSpec ??= File.ReadAllText(start.ArgumentList[3]);
            output(calls.Count == 1 ? "ok" : "==> Creating the solution", false);
            return Task.FromResult(0);
        }, new StudioSettings { Location = folder.Path, CliPath = "C:/tools/Coworkee.Cli.dll", TemplateSource = "templates.nupkg", PackageFeed = "C:/feed" });
        main.NewApp();
        main.Basics.Name = "Shop";
        main.Option("logo").Value = logo;
        main.Errors.ShouldBeEmpty();

        await main.Generate.StartAsync();

        main.Generate.RunState.ShouldBe(RunState.Succeeded);
        calls.Count.ShouldBe(2);
        calls.ShouldAllBe(c => c.FileName == "dotnet" && c.ArgumentList[0] == "C:/tools/Coworkee.Cli.dll" && c.Environment["RestoreAdditionalProjectSources"] == "C:/feed");
        calls[0].ArgumentList.Skip(1).Take(2).ShouldBe(["spec", "validate"]);
        calls[1].ArgumentList.Skip(1).ShouldBe(["new", "--spec", calls[0].ArgumentList[3], "-o", Path.Combine(folder.Path, "Shop"), "--logo", logo, "--template-source", "templates.nupkg"]);
        var spec = SpecJson.Read(writtenSpec!);
        spec.Output.ShouldBeNull();
        spec.Branding.ShouldBeNull();
        main.Generate.Progress.ShouldHaveSingleItem().State.ShouldBe(ProgressState.Done);
    }

    [Fact]
    public async Task A_failing_validation_stops_before_coworkee_new()
    {
        using var folder = new TempFolder();
        var calls = 0;
        var main = Studio.Create(folder, (_, _, _) => Task.FromResult(++calls == 1 ? 1 : 0));
        main.NewApp();
        main.Basics.Name = "Shop";

        await main.Generate.StartAsync();

        main.Generate.RunState.ShouldBe(RunState.Invalid);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_missing_cli_offers_the_installation()
    {
        using var folder = new TempFolder();
        var installed = false;
        var main = Studio.Create(folder, (start, _, _) =>
        {
            if (start.ArgumentList.Contains("install"))
            {
                installed = true;
                return Task.FromResult(0);
            }

            return installed ? Task.FromResult(0) : Studio.NotFound(start, (_, _) => { }, default);
        });
        main.NewApp();
        main.Basics.Name = "Shop";

        await main.Generate.StartAsync();
        main.Generate.IsCliMissing.ShouldBeTrue();

        await main.Generate.InstallCliAsync();
        installed.ShouldBeTrue();
        main.Generate.RunState.ShouldNotBe(RunState.CliMissing);
    }

    [Fact]
    public void The_switches_say_the_same_as_the_spec_without_entities()
    {
        var spec = new AppSpec
        {
            Name = "Shop",
            Output = "C:/work/Shop",
            Options = new() { ["chat"] = false, ["eventBus"] = "rabbitMq" },
            Branding = new BrandingSpec { Title = "Acme Shop", Theme = "Forest" },
        };

        CommandLine.Format(CommandLine.Flags(spec))
            .ShouldBe("coworkee new Shop -o C:/work/Shop --title \"Acme Shop\" --chat false --event-bus rabbitMq --theme Forest");
    }

    [Fact]
    public void Web_media_stays_in_the_spec_and_local_media_goes_on_the_command_line()
    {
        var web = new AppSpec { Name = "Shop", Branding = new BrandingSpec { SignInMedia = "https://example.com/a.mp4" } };
        var local = new AppSpec { Name = "Shop", Branding = new BrandingSpec { SignInMedia = "C:/media/a.mp4" } };

        var (webSpec, webArgs) = CommandLine.New(web, "s.json", null);
        var (localSpec, localArgs) = CommandLine.New(local, "s.json", null);

        webSpec.Branding!.SignInMedia.ShouldBe("https://example.com/a.mp4");
        webArgs.ShouldBe(["new", "--spec", "s.json"]);
        localSpec.Branding.ShouldBeNull();
        localArgs.ShouldBe(["new", "--spec", "s.json", "--sign-in-media", "C:/media/a.mp4"]);
    }

    [Fact]
    public void The_cli_runs_as_tool_executable_or_dll()
    {
        using var folder = new TempFolder();

        CliInvocation.Resolve(null, folder.Path).ShouldBe(new CliInvocation("coworkee", []), new CliInvocationComparer());
        CliInvocation.Resolve("C:/cli/Coworkee.Cli.dll").ShouldSatisfyAllConditions(c => c.FileName.ShouldBe("dotnet"), c => c.Prefix.ShouldBe(["C:/cli/Coworkee.Cli.dll"]));
        CliInvocation.Resolve("/usr/local/bin/coworkee").FileName.ShouldBe("/usr/local/bin/coworkee");

        var tool = Path.Combine(folder.Path, "coworkee" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        File.WriteAllText(tool, string.Empty);
        CliInvocation.Resolve(" ", folder.Path).FileName.ShouldBe(tool);
    }

    private sealed class CliInvocationComparer : IEqualityComparer<CliInvocation>
    {
        public bool Equals(CliInvocation? x, CliInvocation? y) => x!.FileName == y!.FileName && x.Prefix.SequenceEqual(y.Prefix);

        public int GetHashCode(CliInvocation obj) => obj.FileName.GetHashCode(StringComparison.Ordinal);
    }
}

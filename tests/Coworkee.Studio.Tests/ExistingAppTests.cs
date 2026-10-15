using System.Diagnostics;
using System.Text.Json.Nodes;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class ExistingAppTests
{
    private static string App(TempFolder folder, AppSpec? stored)
    {
        var root = Directory.CreateDirectory(Path.Combine(folder.Path, "Shop")).FullName;
        File.WriteAllText(Path.Combine(root, "Shop.slnx"), "<Solution />");
        Directory.CreateDirectory(Path.Combine(root, "src", "Shop.AppHost"));
        File.WriteAllText(Path.Combine(root, "src", "Shop.AppHost", "AppHost.cs"), "builder.AddCoworkeeApp(\"shop\", options =>\n{\n    options.UseSqlite();\n});\n");
        if (stored is not null)
        {
            Directory.CreateDirectory(Path.Combine(root, ".coworkee"));
            File.WriteAllText(ExistingApp.StatePath(root), SpecJson.Write(stored));
        }

        return root;
    }

    [Fact]
    public void Opening_an_app_prefills_the_wizard_with_its_stored_options_and_entities()
    {
        using var folder = new TempFolder();
        var spec = SpecJson.Example();
        spec.Options["reports"] = false;
        var root = App(folder, spec);
        var main = Studio.Create(folder);

        main.OpenApp(Path.Combine(root, "src"));

        main.ExistingApp.ShouldBe(root);
        main.IsExistingApp.ShouldBeTrue();
        main.Basics.Name.ShouldBe("Shop");
        main.Option("reports").IsOn.ShouldBeFalse();
        main.Option("chat").IsOn.ShouldBeFalse();
        main.Option("theme").Value.ShouldBe("Forest");
        main.Entities.Modules.Select(m => m.Name).ShouldBe(["Sales"]);
        main.Current.ShouldBe(main.Review);
        main.NextText.ShouldBe("Show the changes");

        main.NewApp();
        main.IsExistingApp.ShouldBeFalse();
    }

    [Fact]
    public void An_app_without_app_json_shows_the_options_its_files_show()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.OpenApp(App(folder, null));

        main.Option("database").Value.ShouldBe("sqlite");
        main.Status.ShouldBe("Read the options of Shop from its files");
        main.OpenApp(folder.Path);
        main.Status.ShouldStartWith("No Coworkee app in");
    }

    [Fact]
    public async Task The_plan_comes_first_then_apply_runs_coworkee_add_and_lists_the_conflicts()
    {
        using var folder = new TempFolder();
        var root = App(folder, new AppSpec { Name = "Shop", Options = { ["reports"] = false } });
        var calls = new List<ProcessStartInfo>();
        string? written = null;
        var main = Studio.Create(folder, (start, output, _) =>
        {
            calls.Add(start);
            written = File.ReadAllText(start.ArgumentList[3]);
            output(start.ArgumentList.Contains("--dry-run") ? "option   reports: false -> true" : "conflict src/Shop.AppHost/AppHost.cs.rej", false);
            return Task.FromResult(0);
        }, new Model.StudioSettings { Location = folder.Path, CliPath = "C:/tools/Coworkee.Cli.dll" });
        main.OpenApp(root);
        main.Option("reports").Value = "true";

        await main.Generate.StartAsync();

        main.Generate.RunState.ShouldBe(RunState.PlanReady);
        main.Generate.ShowLog.ShouldBeTrue();
        calls.ShouldHaveSingleItem().WorkingDirectory.ShouldBe(root);
        calls[0].ArgumentList.Skip(1).ShouldBe(["add", "--spec", calls[0].ArgumentList[3], "--dry-run"]);
        SpecJson.Read(written!).Options["reports"]!.GetValue<bool>().ShouldBeTrue();

        await main.Generate.ApplyAsync();

        calls.Count.ShouldBe(2);
        calls[1].ArgumentList.ShouldNotContain("--dry-run");
        main.Generate.RunState.ShouldBe(RunState.Succeeded);
        main.Generate.Headline.ShouldBe("Your app is updated");
        main.Generate.Conflicts.ShouldBe(["src/Shop.AppHost/AppHost.cs.rej"]);
        main.Generate.HasConflicts.ShouldBeTrue();
    }

    [Fact]
    public void The_add_command_line_carries_dry_run_and_the_template_source() =>
        CommandLine.Add("spec.json", dryRun: true, "t.nupkg").ShouldBe(["add", "--spec", "spec.json", "--dry-run", "--template-source", "t.nupkg"]);
}

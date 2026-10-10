using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.Tests;

public sealed class SpecFileTests
{
    [Fact]
    public void A_saved_spec_opens_with_the_same_content()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        main.Option("theme").Value = "Sunset";
        main.Branding.UseBrandColors = true;
        main.Option("signInLayout").Value = "Split";
        main.Option("signInMedia").Value = "https://example.com/hero.mp4";
        var file = folder.File("shop.json");

        main.Save(file);
        var saved = File.ReadAllText(file);
        var reopened = Studio.Create(folder);
        reopened.Open(file);

        SpecJson.Write(reopened.ToSpec(folder.Path)).ShouldBe(saved);
        reopened.Current.ShouldBe(reopened.Review);
        reopened.SpecPath.ShouldBe(file);
        reopened.Branding.UseBrandColors.ShouldBeTrue();
        reopened.Settings.RecentSpecs.ShouldBe([file]);
        SpecValidator.Validate(SpecJson.Read(saved), folder.Path).ShouldBeEmpty();
    }

    [Fact]
    public void A_saved_spec_points_to_files_next_to_it_and_to_the_output_below_it()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var logo = elsewhere.File("brand.png");
        File.WriteAllBytes(logo, [137, 80, 78, 71]);
        var main = Scenario.Shop(folder);
        main.Option("logo").Value = logo;

        main.Save(folder.File("shop.json"));

        var spec = SpecJson.Read(File.ReadAllText(folder.File("shop.json")));
        spec.Branding!.Logo.ShouldBe("brand.png");
        spec.Output.ShouldBe("Shop");
        File.Exists(folder.File("brand.png")).ShouldBeTrue();
        SpecValidator.Validate(spec, folder.Path).ShouldBeEmpty();
    }

    [Fact]
    public void Output_outside_the_folder_of_the_spec_is_left_out()
    {
        using var folder = new TempFolder();
        using var elsewhere = new TempFolder();
        var main = Scenario.Shop(folder);
        main.Basics.Location = elsewhere.Path;

        main.Save(folder.File("shop.json"));

        SpecJson.Read(File.ReadAllText(folder.File("shop.json"))).Output.ShouldBeNull();
    }

    [Fact]
    public void A_broken_spec_reports_and_stays_on_the_page()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("broken.json"), "{ nope");
        var main = Studio.Create(folder);

        main.Open(folder.File("broken.json"));

        main.Current.ShouldBe(main.Welcome);
        main.Status.ShouldNotBeNull().ShouldStartWith("Cannot open");
    }

    [Fact]
    public void Settings_keep_recent_specs_newest_first()
    {
        using var folder = new TempFolder();
        var settings = new StudioSettings { CliPath = "coworkee.dll", Language = "de" };
        for (var i = 0; i < 10; i++)
        {
            settings.AddRecent($"spec{i}.json");
        }

        settings.AddRecent("spec3.json");
        settings.Save(folder.File("settings.json"));
        var loaded = StudioSettings.Load(folder.File("settings.json"));

        loaded.RecentSpecs.Count.ShouldBe(8);
        loaded.RecentSpecs[0].ShouldBe("spec3.json");
        loaded.RecentSpecs.Count(f => f == "spec3.json").ShouldBe(1);
        loaded.CliPath.ShouldBe("coworkee.dll");
        loaded.Language.ShouldBe("de");
        StudioSettings.Load(folder.File("missing.json")).RecentSpecs.ShouldBeEmpty();
    }

    [Fact]
    public void Arguments_open_a_spec_and_set_folder_theme_and_language()
    {
        var arguments = StudioArguments.Parse(["--spec", "shop.json", "--output=C:/work", "--theme", "dark", "--lang", "de"]);

        arguments.ShouldBe(new StudioArguments("shop.json", "C:/work", "dark", "de"));
        StudioArguments.Parse(["shop.json"]).Spec.ShouldBe("shop.json");
        StudioArguments.Parse([]).ShouldBe(new StudioArguments(null, null, null, null));
    }
}

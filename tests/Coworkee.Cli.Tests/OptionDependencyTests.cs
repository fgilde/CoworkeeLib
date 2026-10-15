using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class OptionDependencyTests
{
    private static AppSpec Spec(params (string Id, JsonNode? Value)[] options)
    {
        var spec = new AppSpec { Name = "Shop" };
        foreach (var (id, value) in options)
        {
            spec.Options[id] = value;
        }

        return spec;
    }

    [Fact]
    public void Options_that_only_make_sense_with_another_one_say_so()
    {
        OptionCatalog.All.Where(o => o.DependsOn is not null).Select(o => o.Id)
            .ShouldBe(["airworksPackages", "backup", "aiProvider", "aiModel", "aiEndpoint", "signInMedia", "signInMediaSide"]);
        foreach (var option in OptionCatalog.All.Where(o => o.DependsOn is not null))
        {
            var parent = OptionCatalog.Find(option.DependsOn!.Option).ShouldNotBeNull(option.Id);
            parent.Id.ShouldNotBe(option.Id);
            option.DependsOn.Values.ShouldAllBe(v => parent.Choices != null && parent.Choices.Any(c => c.Value == v) || parent.Kind == OptionKind.Bool, option.Id);
            if (option.DependsOn.Or is { } other)
            {
                other.Values.ShouldAllBe(v => OptionCatalog.Find(other.Option)!.Choices!.Any(c => c.Value == v), option.Id);
            }
        }
    }

    [Fact]
    public void AirWorks_packages_are_not_passed_and_reported_without_airworks()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse("new Shop --airworks-packages npm"));

        NewCommand.TemplateArgs(settings).ShouldNotContain("--airworksPackages");
        SpecValidator.Warnings(settings.Spec).ShouldBe(["airworksPackages: ignored, it only matters with airworks (coworkee options)."]);

        var withAirWorks = NewCommand.Read(CliApp.Build().Parse("new Shop --airworks --airworks-packages npm"));
        SpecValidator.Warnings(withAirWorks.Spec).ShouldBeEmpty();
        NewCommand.TemplateArgs(withAirWorks)[^2..].ShouldBe(["--airworksPackages", "npm"]);
    }

    [Fact]
    public void Nothing_is_reported_for_a_value_that_is_the_default_or_not_set()
    {
        SpecValidator.Warnings(Spec()).ShouldBeEmpty();
        SpecValidator.Warnings(Spec(("airworksPackages", "bundled"))).ShouldBeEmpty();
    }

    [Fact]
    public void The_AI_provider_needs_the_AI_assistant_and_the_database_backups_need_postgres()
    {
        var spec = Spec(("ai", false), ("aiProvider", "ollama"), ("aiModel", "llama3.2"), ("database", "sqlite"), ("backup", false));

        SpecValidator.Warnings(spec).ShouldBe(
        [
            "backup: ignored, it only matters with database = postgres (coworkee options).",
            "aiProvider: ignored, it only matters with ai (coworkee options).",
            "aiModel: ignored, it only matters with ai (coworkee options).",
        ]);
        NewCommand.TemplateArgs(new NewSettings(spec, ".", "shop", null)).ShouldBe(
            ["dotnet", "new", "coworkee", "-n", "Shop", "-o", "shop", "--ai", "false", "--database", "sqlite"]);
    }

    [Fact]
    public void Sign_in_media_needs_a_layout_with_media_and_its_side_needs_the_media()
    {
        var card = Spec(("signInLayout", "Card"), ("signInMedia", "https://cdn.test/a.mp4"), ("signInMediaSide", "Right"));

        SpecValidator.Warnings(card).ShouldBe(
        [
            "signInMedia: ignored, it only matters with signInLayout = Split or Slide, or loadingScreen = SignInMedia (coworkee options).",
            "signInMediaSide: ignored, it only matters with signInLayout = Split or Slide, or loadingScreen = SignInMedia (coworkee options).",
        ]);
        NewCommand.TemplateArgs(new NewSettings(card, ".", "shop", null)).ShouldNotContain("--signInMedia");

        SpecValidator.Warnings(Spec(("signInMediaSide", "Right"))).ShouldBe(["signInMediaSide: ignored, it only matters with signInMedia (coworkee options)."]);
        SpecValidator.Warnings(Spec(("signInMedia", "https://cdn.test/a.mp4"), ("signInMediaSide", "Right"))).ShouldBeEmpty();
    }

    [Fact]
    public void Sign_in_media_also_matters_with_the_card_layout_when_the_loading_screen_shows_it()
    {
        var card = Spec(("signInLayout", "Card"), ("signInMedia", "https://cdn.test/a.mp4"), ("loadingScreen", "SignInMedia"));

        SpecValidator.Warnings(card).ShouldBeEmpty();
        NewCommand.TemplateArgs(new NewSettings(card, ".", "shop", null)).ShouldContain("--signInMedia");
        NewCommand.TemplateArgs(new NewSettings(card, ".", "shop", null)).ShouldContain("--loadingScreen");
        var json = JsonDocument.Parse(OptionsCommand.CatalogJson()).RootElement.EnumerateArray().Single(o => o.GetProperty("id").GetString() == "signInMedia");
        json.GetProperty("dependsOn").GetProperty("or").GetProperty("option").GetString().ShouldBe("loadingScreen");
    }

    [Fact]
    public void Options_name_what_they_need_and_link_to_the_products_they_bring()
    {
        var text = OptionsCommand.Text();

        text.ShouldContain("Needs: airworks (otherwise ignored)");
        text.ShouldContain("Needs: database = postgres (otherwise ignored)");
        foreach (var (id, url) in new Dictionary<string, string>
        {
            ["airworks"] = "https://fgilde.github.io/AirWorks/", ["aspireUI"] = "https://fgilde.github.io/AspireUI/", ["webDataStudio"] = "https://fgilde.github.io/WebDataStudio/",
            ["grafana"] = "https://grafana.com/", ["keycloak"] = "https://www.keycloak.org/", ["redis"] = "https://redis.io/", ["elasticsearch"] = "https://www.elastic.co/elasticsearch",
        })
        {
            OptionCatalog.Find(id)!.InfoUrl.ShouldBe(url);
            text.ShouldContain($"More: {url}");
        }

        OptionCatalog.Find("database")!.Choices!.Select(c => c.InfoUrl).ShouldBe(["https://www.postgresql.org/", "https://www.microsoft.com/sql-server", "https://sqlite.org/"]);
        OptionCatalog.Find("aiProvider")!.Choices!.Single(c => c.Value == "ollama").InfoUrl.ShouldNotBeNull();
        OptionCatalog.Find("translationProvider")!.Choices!.Single(c => c.Value == "libreTranslate").InfoUrl.ShouldBe("https://libretranslate.com/");
        text.ShouldContain("More (postgres): https://www.postgresql.org/");
        OptionCatalog.All.SelectMany(o => new[] { o.InfoUrl }.Concat(o.Choices?.Select(c => c.InfoUrl) ?? [])).OfType<string>().ShouldAllBe(u => SpecValidator.IsWebAddress(u));

        var json = JsonDocument.Parse(OptionsCommand.CatalogJson()).RootElement.EnumerateArray().Single(o => o.GetProperty("id").GetString() == "airworksPackages");
        json.GetProperty("dependsOn").GetProperty("option").GetString().ShouldBe("airworks");
    }

    [Fact]
    public void The_summary_lists_the_modules_of_the_library_on_and_off_even_for_a_spec_of_the_defaults()
    {
        var defaults = SpecSummary.Lines(Spec());

        defaults.ShouldContain("Library modules on:  auditing, notifications, chat, reports, ai, backup");
        defaults.ShouldContain("Library modules off: files, features, social, extendedAttributes, elasticsearch, eventBus, clientEntities");
        defaults.ShouldContain("Own modules: -");
    }

    [Fact]
    public void The_summary_of_a_spec_with_every_module_on_names_them_all()
    {
        var modules = OptionCatalog.All.Where(o => o.Group == OptionCatalog.Modules).ToList();
        var spec = Spec([.. modules.Select(o => (o.Id, (JsonNode?)(o.Kind == OptionKind.Bool ? JsonValue.Create(true) : JsonValue.Create("rabbitMq"))))]);
        spec.Modules.Add(new ModuleSpec { Name = "Sales", Entities = [new EntitySpec { Name = "Order" }] });

        var lines = SpecSummary.Lines(spec);

        lines.ShouldContain("Library modules on:  " + string.Join(", ", modules.Select(o => o.Kind == OptionKind.Bool ? o.Id : $"{o.Id}=rabbitMq")));
        lines.ShouldContain("Library modules off: -");
        lines.ShouldContain("Own modules: Sales (Order)");
        lines.ShouldContain(l => l.StartsWith("General: ") && l.Contains("airworksPackages=bundled (unused)"));
    }

    [Fact]
    public void Spec_show_prints_the_summary_and_validate_reports_warnings()
    {
        using var folder = new TempFolder();
        var file = folder.File("shop.json");
        File.WriteAllText(file, SpecJson.Write(Spec(("airworksPackages", "npm"))));

        CliApp.Build().Parse(["spec", "show", file]).Invoke().ShouldBe(0);
        CliApp.Build().Parse(["spec", "validate", file]).Invoke().ShouldBe(0);
    }

    [Fact]
    public void The_spec_schema_says_what_an_option_depends_on_and_what_modules_means()
    {
        var schema = SpecJson.Schema().ToJsonString();

        schema.ShouldContain("Only used with airworks.");
        schema.ShouldContain("Modules of your own with their entities");
    }
}

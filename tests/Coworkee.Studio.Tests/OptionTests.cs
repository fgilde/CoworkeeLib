using Avalonia.Controls;
using Avalonia.Media;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.Tests;

public sealed class OptionTests
{
    [Fact]
    public void A_preset_sets_the_module_switches_and_is_shown_as_active()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var minimal = main.Architecture.Presets.Single(p => p.Preset.Name == "Minimal");

        main.Architecture.ApplyPreset(minimal);

        minimal.IsActive.ShouldBeTrue();
        main.Option("chat").IsOn.ShouldBeFalse();
        main.Option("auditing").IsOn.ShouldBeFalse();
        main.Option("eventBus").Value.ShouldBe("none");
        main.Architecture.Presets.Count(p => p.IsActive).ShouldBe(1);
        main.ToSpec().Options["chat"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Everything_brings_every_container()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Architecture.ApplyPreset(main.Architecture.Presets.Single(p => p.Preset.Name == "Everything"));

        main.Architecture.Summary.Containers.ShouldBe(["PostgreSQL", "Redis", "Mailpit", "Elasticsearch", "RabbitMQ", "Azurite", "Keycloak", "Ollama", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "postgres_exporter", "WebDataStudio", "Kibana", "oauth2-proxy", "AspireUI"]);
        main.Architecture.Summary.Packages.ShouldContain("Coworkee.EventBus.RabbitMq");
        main.Architecture.Summary.Projects.ShouldContain("MyApp.Catalog");
        var modules = main.Architecture.Groups.Single(g => g.Name == OptionCatalog.Modules).Options.Select(o => o.Id).ToList();
        modules.IndexOf("kibana").ShouldBe(modules.IndexOf("elasticsearch") + 1);
    }

    [Fact]
    public void The_airworks_card_links_to_airworks_and_adds_its_project()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        string? opened = null;
        main.Dialogs.Open = url => opened = url;
        var airWorks = main.Option("airworks");

        airWorks.IsOn.ShouldBeFalse();
        airWorks.HasInfo.ShouldBeTrue();
        main.Architecture.Summary.Projects.ShouldNotContain("MyApp.AirWorks");
        airWorks.OpenInfoCommand.Execute(null);
        opened.ShouldBe("https://fgilde.github.io/AirWorks/");

        airWorks.IsOn = true;
        main.Architecture.Summary.Projects.ShouldContain("MyApp.AirWorks");
        main.ToSpec().Options["airworks"]!.GetValue<bool>().ShouldBeTrue();
        main.Option("auditing").HasInfo.ShouldBeFalse();
    }

    [Fact]
    public void AspireUI_is_a_highlighted_option_that_is_off_until_chosen_and_adds_its_container()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.Architecture.Infrastructure.Select(o => o.Id).ShouldBe(["database", "redis", "grafana", "webDataStudio", "aspireUI"]);
        main.Option("aspireUI").IsOn.ShouldBeFalse();
        main.Architecture.Summary.Containers.ShouldNotContain("AspireUI");

        main.Option("aspireUI").IsOn = true;
        main.Architecture.Summary.Containers.ShouldContain("AspireUI");
        main.ToSpec().Options["aspireUI"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Database_choice_replaces_the_postgres_container_and_drops_backups_off_postgres()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.Option("database").Value = "sqlServer";
        main.Architecture.Summary.Containers.ShouldBe(["SQL Server", "Redis", "Mailpit", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "WebDataStudio"]);
        main.Architecture.Summary.Packages.ShouldNotContain("Coworkee.Backup");

        main.Option("database").Value = "sqlite";
        main.Architecture.Summary.Containers.ShouldBe(["Redis", "Mailpit", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "WebDataStudio"]);
        main.ToSpec().Options["database"]!.GetValue<string>().ShouldBe("sqlite");
    }

    [Fact]
    public void Infrastructure_choices_explain_what_they_rule_out()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Option("backup").IsRelevant.ShouldBeTrue();
        main.Option("aiProvider").Note!.ShouldContain("Parameters:ai-apikey");

        main.Option("database").Value = "sqlite";
        main.Option("backup").IsRelevant.ShouldBeFalse();
        main.Option("backup").UnmetText.ShouldBe("Only used with Database: PostgreSQL.");
        main.ToSpec().Options.ShouldNotContainKey("backup");
        main.Option("database").Note!.ShouldContain("One writer at a time");

        main.Option("aiProvider").Value = "ollama";
        main.Option("aiProvider").Note!.ShouldContain("Ollama container");
        main.Option("ai").IsOn = false;
        main.Option("aiProvider").IsAvailable.ShouldBeFalse();
        main.Architecture.Summary.Containers.ShouldNotContain("Ollama");

        main.Option("redis").IsOn = false;
        main.Option("redis").Note!.ShouldContain("one instance");
        main.Option("grafana").IsOn = false;
        main.Option("webDataStudio").IsOn = false;
        main.Option("translationProvider").Value = "none";
        main.Architecture.Summary.Containers.ShouldBe(["Mailpit"]);
    }

    [Fact]
    public void Only_changed_options_go_into_the_spec_but_every_module_is_listed()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        // a module that needs another one which is off by default (elsa without workflows) stays out
        var modules = OptionCatalog.All.Where(o => o.Group == OptionCatalog.Modules && OptionCatalog.Unmet(o, id => OptionCatalog.Find(id)!.Default) is null)
            .Select(o => o.Id).ToList();

        main.ToSpec().Options.Keys.ShouldBe(modules);
        main.ToSpec().Options["chat"]!.GetValue<bool>().ShouldBeTrue();
        main.ToSpec().Options["files"]!.GetValue<bool>().ShouldBeFalse();
        main.ToSpec().Branding.ShouldBeNull();

        main.Option("storage").Value = "azureBlob";
        main.Option("theme").Value = "Forest";
        var spec = main.ToSpec();
        spec.Options.Keys.ShouldBe([.. modules, "storage"]);
        spec.Branding!.Theme.ShouldBe("Forest");
    }

    [Fact]
    public void With_every_module_on_the_spec_at_the_end_names_them_all()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        main.Architecture.ApplyPreset(main.Architecture.Presets.Single(p => p.Preset.Name == "Everything"));

        main.Review.Refresh();

        var options = main.ToSpec().Options;
        foreach (var module in OptionCatalog.All.Where(o => o.Group == OptionCatalog.Modules && o.Kind == OptionKind.Bool))
        {
            options[module.Id]!.GetValue<bool>().ShouldBeTrue(module.Id);
            main.Review.SpecText.ShouldContain($"\"{module.Id}\": true");
        }

        main.Review.SpecText.ShouldContain("\"eventBus\": \"rabbitMq\"");
        main.Review.Items.Single(i => i.Label == "Modules").Value.ShouldContain("Audit log");
        main.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_module_entity_marked_client_entity_still_switches_client_entities_on_in_the_listed_modules()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        main.Entities.AddModule();
        main.Entities.SelectedEntity!.ClientEntity = true;

        main.ToSpec().Options["clientEntities"]!.GetValue<bool>().ShouldBeTrue();
        main.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_airworks_packages_and_launcher_are_not_shown_nor_written_unless_airworks_is_on()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        var packages = main.Option("airworksPackages");
        var general = main.Architecture.Groups.Single(g => g.Name == OptionCatalog.General);

        var launcher = main.Option("airworksLauncher");
        packages.IsRelevant.ShouldBeFalse();
        launcher.IsRelevant.ShouldBeFalse();
        packages.UnmetText.ShouldBe("Only used with AirWorks client: switch it on first.");
        general.Options.ShouldContain(packages);

        packages.Value = "npm";
        main.ToSpec().Options.ShouldNotContainKey("airworksPackages");
        Coworkee.Studio.Cli.CommandLine.Flags(main.ToSpec()).ShouldNotContain("--airworks-packages");

        main.Option("airworks").IsOn = true;
        packages.IsRelevant.ShouldBeTrue();
        packages.UnmetText.ShouldBeNull();
        main.ToSpec().Options["airworksPackages"]!.GetValue<string>().ShouldBe("npm");
        Coworkee.Studio.Cli.CommandLine.Flags(main.ToSpec()).ShouldContain("--airworks-packages");
        launcher.Value = "overlay";
        main.ToSpec().Options["airworksLauncher"]!.GetValue<string>().ShouldBe("overlay");
        Coworkee.Studio.Cli.CommandLine.Flags(main.ToSpec()).ShouldContain("--airworks-launcher");

        main.Option("airworks").IsOn = false;
        packages.IsRelevant.ShouldBeFalse();
        packages.Value.ShouldBe("npm");
    }

    [Fact]
    public void Options_that_need_another_one_follow_it_in_the_spec()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        main.Option("aiModel").Value = "gpt-5";
        main.Option("signInMedia").Value = "https://cdn.test/a.mp4";
        main.Option("signInMediaSide").Value = "Right";

        main.ToSpec().Options["aiModel"]!.GetValue<string>().ShouldBe("gpt-5");
        main.ToSpec().Branding!.SignInMedia.ShouldBe("https://cdn.test/a.mp4");
        main.ToSpec().Branding!.SignInMediaSide.ShouldBe("Right");

        main.Option("ai").IsOn = false;
        main.Option("signInLayout").Value = "Card";
        main.Option("aiModel").IsRelevant.ShouldBeFalse();
        main.ToSpec().Options.ShouldNotContainKey("aiModel");
        main.ToSpec().Branding!.SignInMedia.ShouldBeNull();
        main.ToSpec().Branding!.SignInMediaSide.ShouldBeNull();
        main.Errors.ShouldBeEmpty();

        // the loading screen shows the media with every layout
        main.SignIn.LoadingMedia = true;
        main.ToSpec().Branding!.LoadingScreen.ShouldBe("SignInMedia");
        main.ToSpec().Branding!.SignInMedia.ShouldBe("https://cdn.test/a.mp4");
        main.SignIn.LoadingAnimated = true;

        main.Option("signInLayout").Value = "Split";
        main.ToSpec().Branding!.SignInMedia.ShouldBe("https://cdn.test/a.mp4");
    }

    [Fact]
    public Task Every_option_has_an_icon_that_exists() => TestApp.Ui(() =>
    {
        OptionLooks.Ids.ShouldBe(OptionCatalog.All.Select(o => o.Id), ignoreOrder: true);
        foreach (var option in OptionCatalog.All)
        {
            Avalonia.Application.Current!.TryFindResource(OptionLooks.Icon(option.Id), out var icon).ShouldBeTrue(option.Id);
            icon.ShouldBeOfType<Avalonia.Media.StreamGeometry>();
        }
    });

    [Fact]
    public void Cards_of_products_open_their_website_and_choices_open_the_one_chosen()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        var opened = new List<string>();
        main.Dialogs.Open = opened.Add;

        var expected = new Dictionary<string, string>
        {
            ["airworks"] = "https://fgilde.github.io/AirWorks/", ["aspireUI"] = "https://fgilde.github.io/AspireUI/", ["webDataStudio"] = "https://fgilde.github.io/WebDataStudio/",
            ["grafana"] = "https://grafana.com/", ["keycloak"] = "https://www.keycloak.org/", ["redis"] = "https://redis.io/", ["elasticsearch"] = "https://www.elastic.co/elasticsearch",
        };
        foreach (var (id, url) in expected)
        {
            main.Option(id).HasInfo.ShouldBeTrue(id);
            main.Option(id).OpenInfoCommand.Execute(null);
            opened[^1].ShouldBe(url);
        }

        var database = main.Option("database");
        database.InfoText.ShouldBe("More about PostgreSQL");
        database.OpenInfoCommand.Execute(null);
        opened[^1].ShouldBe("https://www.postgresql.org/");
        database.Value = "sqlite";
        database.InfoText.ShouldBe("More about SQLite");
        database.OpenInfoCommand.Execute(null);
        opened[^1].ShouldBe("https://sqlite.org/");

        main.Option("eventBus").HasInfo.ShouldBeFalse();
        main.Option("eventBus").Value = "rabbitMq";
        main.Option("eventBus").HasInfo.ShouldBeTrue();
        main.Option("chat").HasInfo.ShouldBeFalse();
    }

    [Fact]
    public void Brand_colors_follow_the_switch_and_the_theme()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Option("theme").Value = "Forest";

        main.Branding.UseBrandColors = true;
        main.Option("primaryColor").Value.ShouldBe("#2e7d32");

        main.Branding.UsePrimary(Color.Parse("#c2185b"));
        main.Option("primaryColor").Value.ShouldBe("#c2185b");
        main.Branding.PreviewLight.Palette.Primary.ShouldBe(Color.Parse("#c2185b"));
        main.Branding.PreviewDark.Palette.Primary.ShouldBe(Color.Parse("#c2185b"));

        main.Branding.UseBrandColors = false;
        main.Option("primaryColor").Value.ShouldBeNull();
        main.Option("secondaryColor").Value.ShouldBeNull();
    }

    [Fact]
    public void Contrast_text_is_computed_like_in_the_app()
    {
        Palette.ContrastText(Color.Parse("#1565c0")).ShouldBe(Colors.White);
        Palette.ContrastText(Color.Parse("#ffd54f")).ShouldBe(Colors.Black);
    }

    [Fact]
    public void Every_theme_of_the_cli_has_a_preview()
    {
        Coworkee.Cli.Spec.OptionCatalog.Themes.ShouldBe(ThemeInfo.All.Select(t => t.Name));
    }
}

using Avalonia.Media;
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

        main.Architecture.Summary.Containers.ShouldBe(["PostgreSQL", "Redis", "Mailpit", "Elasticsearch", "RabbitMQ", "Azurite", "Keycloak", "Ollama", "LibreTranslate", "Grafana", "Prometheus", "Loki", "Tempo", "OpenTelemetry Collector", "postgres_exporter", "WebDataStudio"]);
        main.Architecture.Summary.Packages.ShouldContain("Coworkee.EventBus.RabbitMq");
        main.Architecture.Summary.Projects.ShouldContain("MyApp.Catalog");
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
        main.Option("backup").IsAvailable.ShouldBeTrue();
        main.Option("aiProvider").Note!.ShouldContain("Parameters:ai-apikey");

        main.Option("database").Value = "sqlite";
        main.Option("backup").IsAvailable.ShouldBeFalse();
        main.Option("backup").Note!.ShouldContain("PostgreSQL");
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
    public void Only_changed_options_go_into_the_spec()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";

        main.ToSpec().Options.ShouldBeEmpty();
        main.ToSpec().Branding.ShouldBeNull();

        main.Option("storage").Value = "azureBlob";
        main.Option("theme").Value = "Forest";
        var spec = main.ToSpec();
        spec.Options.Keys.ShouldBe(["storage"]);
        spec.Branding!.Theme.ShouldBe("Forest");
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

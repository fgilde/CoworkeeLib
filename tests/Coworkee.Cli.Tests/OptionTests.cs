using System.Text.Json;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class OptionTests
{
    private static readonly JsonElement Symbols = JsonDocument.Parse(File.ReadAllText(Path.Combine(
        Dotnet.RepositoryRoot(), "templates", "Coworkee.Templates", "content", "CoworkeeApp", ".template.config", "template.json"))).RootElement.GetProperty("symbols");

    private static IEnumerable<JsonProperty> Parameters => Symbols.EnumerateObject().Where(s => s.Value.GetProperty("type").GetString() == "parameter");

    [Fact]
    public void Every_template_parameter_has_a_catalog_entry_and_the_other_way_round()
    {
        OptionCatalog.All.Where(o => o.TemplateSymbol).Select(o => o.Id).ShouldBe(Parameters.Select(p => p.Name), ignoreOrder: true);
        OptionCatalog.All.Select(o => o.Id).ShouldBeUnique();
        OptionCatalog.All.Select(o => o.Flag).ShouldBeUnique();
    }

    [Fact]
    public void Flags_are_kebab_case_with_acronyms_kept_together()
    {
        OptionCatalog.Find("aspireUI")!.Flag.ShouldBe("--aspire-ui");
        OptionCatalog.Find("webDataStudio")!.Flag.ShouldBe("--web-data-studio");
    }

    [Fact]
    public void The_airworks_option_is_off_by_default_and_links_to_airworks()
    {
        var option = OptionCatalog.Find("airworks")!;

        option.Flag.ShouldBe("--airworks");
        option.Default.ShouldBe("false");
        option.InfoUrl.ShouldBe("https://fgilde.github.io/AirWorks/");
        OptionsCommand.Text().ShouldContain("More: https://fgilde.github.io/AirWorks/");
        OptionsCommand.CatalogJson().ShouldContain("\"infoUrl\": \"https://fgilde.github.io/AirWorks/\"");
    }

    [Fact]
    public void No_option_shadows_a_switch_of_dotnet_new()
    {
        string[] reserved = ["name", "output", "search", "list", "install", "uninstall", "update", "force", "language", "lang", "type", "project", "help", "verbosity", "diagnostics", "dry-run", "no-update-check"];

        OptionCatalog.All.Select(o => o.Id.ToLowerInvariant()).Intersect(reserved).ShouldBeEmpty();
    }

    [Fact]
    public void Catalog_kinds_defaults_and_choices_match_the_template()
    {
        foreach (var parameter in Parameters)
        {
            var option = OptionCatalog.Find(parameter.Name)!;
            var datatype = parameter.Value.GetProperty("datatype").GetString();
            datatype.ShouldBe(option.Kind switch { OptionKind.Bool => "bool", OptionKind.Choice => "choice", _ => "string" }, option.Id);
            var templateDefault = parameter.Value.TryGetProperty("defaultValue", out var value) && value.GetString() is { Length: > 0 } text ? text : null;
            templateDefault.ShouldBe(option.Default, option.Id);
            if (option is { Kind: OptionKind.Choice, Choices: { } choices })
            {
                parameter.Value.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("choice").GetString()).ShouldBe(choices.Select(c => c.Value), option.Id);
            }
        }
    }

    [Fact]
    public void Catalog_entries_explain_themselves()
    {
        OptionCatalog.All.ShouldAllBe(o => o.Title.Length > 0 && o.Description.Length > 0 && o.WhenToUse.Length > 0 && o.Pros.Count > 0 && o.Cons.Count > 0);
        OptionCatalog.All.Where(o => o.Kind == OptionKind.Choice).ShouldAllBe(o => o.Choices!.Any(c => c.Value == o.Default));
        OptionCatalog.Find("eventBus")!.Flag.ShouldBe("--event-bus");
        OptionCatalog.Find("signInMediaSide")!.Flag.ShouldBe("--sign-in-media-side");
    }

    public static TheoryData<string> TemplateOptions => [.. OptionCatalog.All.Where(o => o.TemplateSymbol).Select(o => o.Id)];

    [Theory]
    [MemberData(nameof(TemplateOptions))]
    public void Each_switch_reaches_the_template_with_a_non_default_value(string id)
    {
        using var folder = new TempFolder();
        var option = OptionCatalog.Find(id)!;
        var (given, expected) = option.Kind switch
        {
            OptionKind.Bool => (option.Default == "true" ? "false" : "true", option.Default == "true" ? "false" : "true"),
            OptionKind.Choice => (option.Choices!.First(c => c.Value != option.Default).Value, option.Choices!.First(c => c.Value != option.Default).Value),
            OptionKind.Color => ("#123abc", "#123abc"),
            OptionKind.Multi => ("en", "en"),
            OptionKind.File when id == "logo" => (folder.File("brand/logo.png"), "logo.png"),
            OptionKind.File => (folder.File("brand/welcome.mp4"), "/media/welcome.mp4"),
            _ when id == "aiEndpoint" => ("https://llm.acme.test/v1", "https://llm.acme.test/v1"),
            _ when id == "repository" => ("acme/shop", "acme/shop"),
            _ => ("Acme Shop", "Acme Shop"),
        };

        var settings = NewCommand.Read(CliApp.Build().Parse(["new", "Shop", .. Needs(option), option.Flag, given]));

        NewCommand.TemplateArgs(settings).ShouldContain($"--{id}");
        var args = NewCommand.TemplateArgs(settings);
        args[Array.IndexOf(args, $"--{id}") + 1].ShouldBe(expected);
    }

    // the switches that make an option matter: its dependency, and that one's own
    private static string[] Needs(CliOption option) => option.DependsOn is { } need && OptionCatalog.Find(need.Option) is { } parent
        ? [.. Needs(parent), parent.Flag, parent.Kind == OptionKind.File ? "https://cdn.test/welcome.mp4" : need.Values.FirstOrDefault() ?? "true"]
        : [];

    [Fact]
    public void A_web_address_as_sign_in_media_is_passed_as_it_is()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse("new Shop --sign-in-media https://cdn.test/welcome.mp4 --sign-in-layout Split"));

        NewCommand.TemplateArgs(settings).ShouldContain("https://cdn.test/welcome.mp4");
    }

    [Fact]
    public void Options_print_as_text_and_json()
    {
        OptionsCommand.Text().ShouldContain("--event-bus");
        var json = JsonDocument.Parse(OptionsCommand.CatalogJson()).RootElement;
        json.GetArrayLength().ShouldBe(OptionCatalog.All.Count);
        json[0].GetProperty("whenToUse").GetString().ShouldNotBeNullOrEmpty();
        JsonDocument.Parse(OptionsCommand.CatalogJson("Branding")).RootElement.EnumerateArray().ShouldAllBe(o => o.GetProperty("group").GetString() == "Branding");
    }
}

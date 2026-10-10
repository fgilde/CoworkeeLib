using System.Text.Json.Nodes;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class SpecTests
{
    [Fact]
    public void The_example_is_valid_and_survives_a_round_trip()
    {
        var example = SpecJson.Example();

        SpecValidator.Validate(example).ShouldBeEmpty();
        SpecJson.Write(SpecJson.Read(SpecJson.Write(example))).ShouldBe(SpecJson.Write(example));
        SpecJson.Write(example).ShouldContain("\"type\": \"decimal\"");
        SpecJson.Write(example).ShouldContain("\"mapping\": \"mapperly\"");
    }

    [Fact]
    public void Options_resolve_from_defaults_options_branding_and_entities()
    {
        var spec = SpecJson.Read("""
            {
              "name": "Shop",
              "options": { "chat": false, "theme": "forest", "eventBus": "RABBITMQ", "title": "Ignored" },
              "branding": { "title": "Acme", "primaryColor": "#0a7d5a" },
              "modules": [ { "name": "Sales", "entities": [ { "name": "Order", "clientEntity": true, "properties": [ { "name": "Number" } ] } ] } ]
            }
            """);

        var values = SpecOptions.Resolve(spec);

        values["chat"].ShouldBe("false");
        values["samples"].ShouldBe("true");
        values["theme"].ShouldBe("Forest");
        values["eventBus"].ShouldBe("rabbitMq");
        values["title"].ShouldBe("Acme");
        values["primaryColor"].ShouldBe("#0a7d5a");
        values["clientEntities"].ShouldBe("true");
        SpecOptions.Mapping(values, spec.Modules[0].Entities[0]).ShouldBe(MappingStrategy.Manual);
    }

    [Fact]
    public void Validation_names_every_problem_with_its_path()
    {
        var spec = new AppSpec
        {
            Name = "my.app",
            Options = { ["nope"] = true, ["chat"] = "maybe", ["eventBus"] = "kafka", ["clientEntities"] = false },
            Branding = new BrandingSpec { Title = "Forest", PrimaryColor = "red", Logo = "missing.svg" },
            Modules =
            [
                new ModuleSpec { Name = "Api", Entities = [] },
                new ModuleSpec
                {
                    Name = "Sales",
                    Entities =
                    [
                        new EntitySpec { Name = "Order", ClientEntity = true, Properties = [new PropertySpec { Name = "Id" }, new PropertySpec { Name = "Total", Type = PropertyType.Decimal, MaxLength = 5 }] },
                        new EntitySpec { Name = "Order", Properties = [new PropertySpec { Name = "A" }, new PropertySpec { Name = "a" }] },
                        new EntitySpec { Name = "Brand", Properties = [] },
                    ],
                },
            ],
        };

        var errors = SpecValidator.Validate(spec, Path.GetTempPath());

        errors.ShouldContain(e => e.StartsWith("name:"));
        errors.ShouldContain(e => e.StartsWith("options.nope:"));
        errors.ShouldContain(e => e.StartsWith("chat:"));
        errors.ShouldContain(e => e.StartsWith("eventBus:"));
        errors.ShouldContain(e => e.StartsWith("primaryColor:"));
        errors.ShouldContain(e => e.StartsWith("logo:"));
        errors.ShouldContain(e => e.StartsWith("branding.title:"));
        errors.ShouldContain(e => e.StartsWith("options.clientEntities:"));
        errors.ShouldContain(e => e.StartsWith("modules[0].name:") && e.Contains("taken"));
        errors.ShouldContain(e => e.StartsWith("modules[0].entities:"));
        errors.ShouldContain(e => e.StartsWith("modules[1].entities[0].properties[0].name:"));
        errors.ShouldContain(e => e.StartsWith("modules[1].entities[0].properties[1].maxLength:"));
        errors.ShouldContain(e => e.StartsWith("modules[1].entities[1].name:") && e.Contains("twice"));
        errors.ShouldContain(e => e.StartsWith("modules[1].entities[1].properties[1].name:") && e.Contains("twice"));
        errors.ShouldContain(e => e.StartsWith("modules[1].entities[2].name:") && e.Contains("samples"));
        errors.ShouldContain(e => e.StartsWith("modules[1].entities[2].properties:"));
    }

    [Fact]
    public void Web_addresses_and_the_default_logo_need_no_file()
    {
        var spec = new AppSpec { Name = "Shop", Branding = new BrandingSpec { SignInMedia = "https://cdn.test/a.mp4", Logo = "coworkee-icon.svg" } };

        SpecValidator.Validate(spec, Path.GetTempPath()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Order", "Orders")]
    [InlineData("Category", "Categories")]
    [InlineData("Day", "Days")]
    [InlineData("Address", "Addresses")]
    [InlineData("Box", "Boxes")]
    [InlineData("Batch", "Batches")]
    public void Plurals_are_naive_english(string name, string plural) => EntitySpec.Pluralize(name).ShouldBe(plural);

    [Fact]
    public void The_schema_describes_the_spec_and_every_option()
    {
        var schema = SpecJson.Schema();

        schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["name"]);
        var options = schema["properties"]!["options"]!["properties"]!.AsObject();
        options.Select(o => o.Key).ShouldBe(OptionCatalog.All.Select(o => o.Id));
        options["eventBus"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["none", "inProcess", "rabbitMq"]);
        options["primaryColor"]!["pattern"]!.GetValue<string>().ShouldBe(SpecValidator.ColorPattern);
        options["chat"]!["default"]!.GetValue<bool>().ShouldBeTrue();
        schema["properties"]!["name"]!["description"]!.GetValue<string>().ShouldContain("letter");
        schema.ToJsonString().ShouldContain("\"decimal\"");
        schema.ToJsonString().ShouldContain("\"mapperly\"");
    }

    [Fact]
    public void A_spec_file_is_read_relative_to_its_folder_and_switches_win()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("assets/logo.svg"), "<svg/>");
        var specFile = folder.File("app.json");
        File.WriteAllText(specFile, """
            // comments are fine
            { "name": "Crm", "output": "out/crm", "options": { "chat": false }, "branding": { "logo": "assets/logo.svg" }, }
            """);

        var settings = NewCommand.Read(CliApp.Build().Parse(["new", "--spec", specFile, "--chat", "--theme", "Sunset"]));

        settings.Spec.Name.ShouldBe("Crm");
        settings.Output.ShouldBe(Path.Combine(folder.Path, "out", "crm"));
        settings.BaseDirectory.ShouldBe(folder.Path);
        var args = NewCommand.TemplateArgs(settings);
        args.ShouldContain("--logo");
        args.ShouldContain("logo.svg");
        args.ShouldContain("Sunset");
        args.ShouldNotContain("--chat");
        Should.Throw<CliException>(() => NewCommand.Read(CliApp.Build().Parse(["new", "--spec", specFile, "--primary-color", "blue"]))).Message.ShouldContain("primaryColor");
        Should.Throw<CliException>(() => NewCommand.Read(CliApp.Build().Parse("new"))).Message.ShouldContain("name:");
    }

    [Fact]
    public void Spec_commands_print_schema_example_and_validate()
    {
        using var folder = new TempFolder();
        var good = folder.File("good.json");
        File.WriteAllText(good, SpecJson.Write(SpecJson.Example()));
        var bad = folder.File("bad.json");
        File.WriteAllText(bad, """{ "name": "1" }""");

        CliApp.Build().Parse(["spec", "validate", good]).Invoke().ShouldBe(0);
        CliApp.Build().Parse(["spec", "validate", bad]).Invoke().ShouldBe(1);
        CliApp.Build().Parse("spec schema").Invoke().ShouldBe(0);
        CliApp.Build().Parse("spec example").Invoke().ShouldBe(0);
        JsonNode.Parse(SpecJson.Write(SpecJson.Example()))!["name"]!.GetValue<string>().ShouldBe("Shop");
    }
}

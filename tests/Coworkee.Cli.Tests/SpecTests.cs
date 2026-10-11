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
        SpecJson.Write(example).ShouldContain("\"type\": \"money\"");
        SpecJson.Write(example).ShouldContain("\"reference\": \"Customer\"");
        SpecOptions.Resolve(example)["languages"].ShouldBe("en,de");
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

    [Theory]
    [InlineData("Acme \"Shop")]
    [InlineData("Acme\\Shop")]
    [InlineData("<script>")]
    [InlineData("a@DateTime.Now")]
    [InlineData("Line\nbreak")]
    public void Titles_that_could_break_out_of_code_or_html_are_refused(string title)
    {
        SpecValidator.IsSafeTitle(title).ShouldBeFalse();
        SpecValidator.Validate(new AppSpec { Name = "Shop", Branding = new BrandingSpec { Title = title } }).ShouldContain(e => e.StartsWith("title:"));
        Should.Throw<CliException>(() => NewCommand.Read(CliApp.Build().Parse(["new", "Shop", "--title", title]))).Message.ShouldContain("title:");
        SpecValidator.IsSafeTitle("Acme Shop & Co. {1}").ShouldBeTrue();
    }

    [Fact]
    public void Spec_files_and_output_stay_inside_the_folder_of_the_spec_and_files_are_images_or_videos()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("brand/logo.svg"), "<svg/>");
        File.WriteAllText(folder.File("brand/run.exe"), "MZ");
        var outside = folder.File("../coworkee-outside-" + Guid.NewGuid().ToString("N") + ".svg");
        File.WriteAllText(outside, "<svg/>");
        try
        {
            string[] Errors(BrandingSpec branding, string? output = null) =>
                [.. SpecValidator.Validate(new AppSpec { Name = "Shop", Output = output, Branding = branding }, Path.Combine(folder.Path, "brand"))];

            Errors(new BrandingSpec { Logo = "logo.svg" }).ShouldBeEmpty();
            Errors(new BrandingSpec { Logo = "../" + Path.GetFileName(outside) }).ShouldContain(e => e.StartsWith("logo:") && e.Contains("inside the folder"));
            Errors(new BrandingSpec { Logo = outside }).ShouldContain(e => e.StartsWith("logo:") && e.Contains("inside the folder"));
            Errors(new BrandingSpec { Logo = "run.exe" }).ShouldContain(e => e.StartsWith("logo:") && e.Contains("no image"));
            Errors(new BrandingSpec { Logo = "https://cdn.test/logo.svg" }).ShouldContain(e => e.StartsWith("logo:"));
            Errors(new BrandingSpec { SignInMedia = "run.exe" }).ShouldContain(e => e.StartsWith("signInMedia:") && e.Contains("video"));
            Errors(new BrandingSpec(), "../app").ShouldContain(e => e.StartsWith("output:"));
            Errors(new BrandingSpec(), Path.GetFullPath("app")).ShouldContain(e => e.StartsWith("output:"));
            Errors(new BrandingSpec(), "apps/shop").ShouldBeEmpty();
            SpecValidator.Validate(new AppSpec { Name = "Shop", Branding = new BrandingSpec { Logo = outside } }, folder.Path, [outside]).ShouldBeEmpty();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void A_logo_typed_on_the_command_line_may_live_anywhere_but_one_in_a_spec_may_not()
    {
        using var folder = new TempFolder();
        var logo = folder.File("logo.svg");
        File.WriteAllText(logo, "<svg/>");
        var spec = folder.File("spec/app.json");
        File.WriteAllText(spec, $$"""{ "name": "Shop", "branding": { "logo": {{System.Text.Json.JsonSerializer.Serialize(logo)}} } }""");

        NewCommand.Read(CliApp.Build().Parse(["new", "Shop", "--logo", logo])).TrustedFiles.ShouldBe([logo]);
        Should.Throw<CliException>(() => NewCommand.Read(CliApp.Build().Parse(["new", "--spec", spec]))).Message.ShouldContain("logo:");
        NewCommand.Read(CliApp.Build().Parse(["new", "--spec", spec, "--logo", logo])).Spec.Options["logo"]!.GetValue<string>().ShouldBe(logo);
    }

    [Fact]
    public void Assets_copy_a_logo_named_like_the_default_and_refuse_files_outside_the_spec_folder()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("coworkee-icon.svg"), "<svg id=\"mine\"/>");
        var output = Path.Combine(folder.Path, "out");
        var spec = new AppSpec { Name = "Shop", Branding = new BrandingSpec { Logo = "coworkee-icon.svg" } };

        Assets.Copy(new NewSettings(spec, folder.Path, output, null), SpecOptions.Resolve(spec));

        File.ReadAllText(Path.Combine(output, "src/Shop.Web.Client/wwwroot/coworkee-icon.svg")).ShouldContain("mine");
        var outside = new AppSpec { Name = "Shop", Branding = new BrandingSpec { Logo = Path.Combine(folder.Path, "coworkee-icon.svg") } };
        Should.Throw<CliException>(() => Assets.Copy(new NewSettings(outside, Path.Combine(folder.Path, "out"), output, null), SpecOptions.Resolve(outside)));
        Assets.Copy(new NewSettings(outside, Path.Combine(folder.Path, "out"), output, null, [Path.Combine(folder.Path, "coworkee-icon.svg")]), SpecOptions.Resolve(outside));
    }

    [Fact]
    public void Web_addresses_need_no_file_but_a_given_logo_does_even_with_the_default_name()
    {
        SpecValidator.Validate(new AppSpec { Name = "Shop", Branding = new BrandingSpec { SignInMedia = "https://cdn.test/a.mp4" } }, Path.GetTempPath()).ShouldBeEmpty();
        SpecValidator.Validate(new AppSpec { Name = "Shop", Branding = new BrandingSpec { Logo = "coworkee-icon.svg" } }, Path.GetTempPath())
            .ShouldContain(e => e.StartsWith("logo:") && e.Contains("no existing file"));
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

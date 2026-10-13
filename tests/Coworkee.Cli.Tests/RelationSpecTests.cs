using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class RelationSpecTests
{
    private static AppSpec App(params ModuleSpec[] modules) => new() { Name = "Shop", Options = { ["samples"] = false }, Modules = [.. modules], Enums = [new EnumSpec { Name = "OrderStatus", Values = ["Open", "Paid"] }] };

    private static ModuleSpec Module(string name, params EntitySpec[] entities) => new() { Name = name, Entities = [.. entities] };

    private static EntitySpec Entity(string name, params PropertySpec[] properties) => new() { Name = name, Properties = [new PropertySpec { Name = "Name", Required = true, MaxLength = 100 }, .. properties] };

    private static PropertySpec Reference(string name, string target, bool required = false) => new() { Name = name, Type = PropertyType.Reference, Reference = target, Required = required };

    [Fact]
    public void References_enums_and_the_new_types_validate_across_modules()
    {
        var spec = App(
            Module("Crm", Entity("Customer", new PropertySpec { Name = "Email", Type = PropertyType.Email, Unique = true })),
            Module("Sales",
                Entity("Order",
                    Reference("Customer", "Crm.Customer", required: true),
                    new PropertySpec { Name = "Status", Type = PropertyType.Enum, Enum = "OrderStatus", Default = "Paid", Facet = true },
                    new PropertySpec { Name = "Total", Type = PropertyType.Money, Default = "12.50" },
                    new PropertySpec { Name = "Due", Type = PropertyType.DateOnly },
                    new PropertySpec { Name = "At", Type = PropertyType.TimeOnly },
                    new PropertySpec { Name = "Site", Type = PropertyType.Url, Label = "Web site" },
                    new PropertySpec { Name = "Note", Type = PropertyType.Text, Searchable = false }),
                Entity("Line", Reference("Order", "Order", required: true) is var order ? Set(order, p => (p.Inverse, p.OnDelete) = ("Lines", DeleteBehavior.Cascade)) : order)));

        SpecValidator.Validate(spec).ShouldBeEmpty();
    }

    [Fact]
    public void Broken_references_enums_defaults_and_flags_are_named()
    {
        var spec = App(
            Module("Crm", Entity("Customer", Reference("Order", "Sales.Order"))),
            Module("Sales",
                Entity("Order",
                    Reference("Customer", "Customer", required: true) is var customer ? Set(customer, p => (p.OnDelete, p.Inverse) = (DeleteBehavior.SetNull, "Orders")) : customer,
                    Reference("Ghost", "Nobody"),
                    new PropertySpec { Name = "Status", Type = PropertyType.Enum, Enum = "Missing" },
                    new PropertySpec { Name = "Kind", Type = PropertyType.Enum, Enum = "OrderStatus", Default = "Closed" },
                    new PropertySpec { Name = "Count", Type = PropertyType.Int, Default = "many" },
                    new PropertySpec { Name = "Note", Type = PropertyType.Text, Unique = true, Default = "a\"b" },
                    new PropertySpec { Name = "When", Type = PropertyType.DateTime, Facet = true, Default = "now" },
                    new PropertySpec { Name = "Flag", Type = PropertyType.Bool, Searchable = true, Label = "<b>" },
                    new PropertySpec { Name = "Total", Type = PropertyType.Decimal, Precision = 5, Scale = 8, MaxLength = 3 },
                    new PropertySpec { Name = "CustomerId" },
                    new PropertySpec { Name = "class" })));

        var errors = SpecValidator.Validate(spec);

        var at = "modules[1].entities[0].properties";
        errors.ShouldContain(e => e.StartsWith("modules:") && e.Contains("references a module that references it back"));
        errors.ShouldContain(e => e.StartsWith($"{at}[1].onDelete:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[1].inverse:") && e.Contains("same module"));
        errors.ShouldContain(e => e.StartsWith($"{at}[2].reference:") && e.Contains("'Nobody'"));
        errors.ShouldContain(e => e.StartsWith($"{at}[3].enum:") && e.Contains("'Missing'"));
        errors.ShouldContain(e => e.StartsWith($"{at}[4].default:") && e.Contains("no value of OrderStatus"));
        errors.ShouldContain(e => e.StartsWith($"{at}[5].default:") && e.Contains("no int"));
        errors.ShouldContain(e => e.StartsWith($"{at}[6].unique:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[6].default:") && e.Contains("quotes"));
        errors.ShouldContain(e => e.StartsWith($"{at}[7].facet:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[7].default:") && e.Contains("cannot be a default"));
        errors.ShouldContain(e => e.StartsWith($"{at}[8].searchable:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[8].label:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[9].precision:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[9].maxLength:"));
        errors.ShouldContain(e => e.StartsWith($"{at}[10].name:") && e.Contains("twice"));
        errors.ShouldContain(e => e.StartsWith($"{at}[11].name:") && e.Contains("keyword"));
    }

    [Fact]
    public void Enums_and_entities_must_not_hide_types_and_need_values()
    {
        var spec = new AppSpec
        {
            Name = "Shop",
            Modules = [Module("Sales", Entity("Task"))],
            Enums = [new EnumSpec { Name = "Severity", Values = ["Low", "low", "new"] }, new EnumSpec { Name = "Empty" }, new EnumSpec { Name = "Task", Values = ["A"] }],
        };

        var errors = SpecValidator.Validate(spec);

        errors.ShouldContain(e => e.StartsWith("modules[0].entities[0].name:") && e.Contains("hide a type"));
        errors.ShouldContain(e => e.StartsWith("enums[0].name:") && e.Contains("hide a type"));
        errors.ShouldContain(e => e.StartsWith("enums[0].values[1]:") && e.Contains("twice"));
        errors.ShouldContain(e => e.StartsWith("enums[0].values[2]:") && e.Contains("keyword"));
        errors.ShouldContain(e => e.StartsWith("enums[1].values:"));
        errors.ShouldContain(e => e.StartsWith("enums[2].name:") && e.Contains("twice"));
        SpecValidator.Validate(new AppSpec { Name = "Shop", Enums = [new EnumSpec { Name = "Kind", Values = ["A"] }] }).ShouldContain(e => e.StartsWith("enums:"));
    }

    [Fact]
    public void A_reference_needs_a_text_to_show_and_existing_entities_of_the_app_count()
    {
        var target = new EntitySpec { Name = "Counter", Properties = [new PropertySpec { Name = "Value", Type = PropertyType.Int }] };
        SpecValidator.Validate(App(Module("Sales", target, Entity("Order", Reference("Counter", "Counter")))))
            .ShouldContain(e => e.Contains(".referenceDisplay:") && e.Contains("Counter has no string"));

        var spec = App(Module("Sales", Entity("Order", Reference("Brand", "Catalog.Brand"))));
        SpecValidator.Validate(spec).ShouldContain(e => e.Contains(".reference:"));
        SpecValidator.Validate(spec, null, [], new SpecIndex(spec, [new KnownEntity("Catalog", "Brand", "Brands", "Name")])).ShouldBeEmpty();
    }

    [Fact]
    public void Languages_come_from_the_shipped_ones_and_contain_the_default()
    {
        string[] Errors(object languages, string? language = null)
        {
            var spec = new AppSpec { Name = "Shop", Options = { ["languages"] = System.Text.Json.Nodes.JsonValue.Create(languages.ToString()) } };
            if (language is not null)
            {
                spec.Options["defaultLanguage"] = language;
            }

            return [.. SpecValidator.Validate(spec)];
        }

        Errors("de, en", "de").ShouldBeEmpty();
        Errors("de").ShouldContain(e => e.StartsWith("defaultLanguage:"));
        Errors("en,fr,zh-Hant", "fr").ShouldBeEmpty();
        Errors("en,xx").ShouldContain(e => e.StartsWith("languages:"));
        Errors("en,en").ShouldContain(e => e.StartsWith("languages:"));
        SpecOptions.Resolve(SpecJson.Read("""{ "name": "Shop", "options": { "languages": ["de", "en"] } }"""))["languages"].ShouldBe("de,en");
        OptionCatalog.ShippedLanguages.ShouldBe(ShippedCultures(), ignoreOrder: true);
        OptionCatalog.ShippedLanguages.ShouldAllBe(l => OptionCatalog.Languages.Any(c => c.Value == l));
    }

    [Fact]
    public void Languages_without_shipped_texts_need_a_translation_service_and_ai_needs_the_assistant()
    {
        string[] Errors(string languages, string provider, bool ai = true) =>
            [.. SpecValidator.Validate(new AppSpec { Name = "Shop", Options = { ["languages"] = languages, ["translationProvider"] = provider, ["ai"] = ai } })];

        Errors("en,de", "none").ShouldBeEmpty();
        Errors("en,fr,ja", "none").ShouldBeEmpty();
        Errors("en,fi", "none").ShouldContain(e => e.StartsWith("translationProvider:") && e.Contains("fi"));
        Errors("en,fi", "myMemory").ShouldBeEmpty();
        Errors("en,fi", "ai", ai: false).ShouldContain(e => e.StartsWith("translationProvider:") && e.Contains("ai"));
        Errors("en", "x").ShouldContain(e => e.StartsWith("translationProvider:"));
        SpecValidator.Validate(new AppSpec { Name = "Shop", Options = { ["translationSource"] = "fr" } }).ShouldContain(e => e.StartsWith("translationSource:"));
    }

    [Theory]
    [InlineData("Customer:ref=Crm.Customer:required:onDelete=cascade:inverse=Orders", PropertyType.Reference)]
    [InlineData("Status:enum=OrderStatus:default=Open:facet", PropertyType.Enum)]
    [InlineData("Total:money:precision=12:scale=3:label=Grand total", PropertyType.Money)]
    [InlineData("Mail:email:unique:display:searchable=false", PropertyType.Email)]
    [InlineData("Due:dateOnly", PropertyType.DateOnly)]
    public void Rich_properties_parse_from_the_command_line(string text, PropertyType type)
    {
        var property = FeatureOptions.Property(text);

        property.Type.ShouldBe(type);
        FeatureOptions.Property("Customer:ref=Crm.Customer:required:onDelete=cascade:inverse=Orders").ShouldSatisfyAllConditions(
            p => p.Reference.ShouldBe("Crm.Customer"), p => p.OnDelete.ShouldBe(DeleteBehavior.Cascade), p => p.Inverse.ShouldBe("Orders"), p => p.Required.ShouldBeTrue());
        FeatureOptions.Property("Total:money:precision=12:scale=3:label=Grand total").ShouldSatisfyAllConditions(
            p => p.Precision.ShouldBe(12), p => p.Scale.ShouldBe(3), p => p.Label.ShouldBe("Grand total"));
        FeatureOptions.Property("Mail:email:unique:display:searchable=false").ShouldSatisfyAllConditions(
            p => p.Unique.ShouldBeTrue(), p => p.Display.ShouldBeTrue(), p => p.Searchable.ShouldBe(false));
        FeatureOptions.EnumSpec("OrderStatus=Open, Paid,Shipped").Values.ShouldBe(["Open", "Paid", "Shipped"]);
        Should.Throw<CliException>(() => FeatureOptions.Property("X:onDelete=sometimes"));
        Should.Throw<CliException>(() => FeatureOptions.Property("X:color=red"));
        Should.Throw<CliException>(() => FeatureOptions.EnumSpec("OrderStatus"));
    }

    private static PropertySpec Set(PropertySpec property, Action<PropertySpec> change)
    {
        change(property);
        return property;
    }

    // cultures the library ships: Localization/{culture}.json of its modules, English being the keys
    private static IEnumerable<string> ShippedCultures() =>
        Directory.EnumerateFiles(Path.Combine(Dotnet.RepositoryRoot(), "src"), "*.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) == "Localization" && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(Path.GetFileNameWithoutExtension).OfType<string>().Append("en").Distinct();
}

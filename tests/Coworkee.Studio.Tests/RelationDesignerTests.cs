using Coworkee.Cli.Spec;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class RelationDesignerTests
{
    [Fact]
    public void References_enums_new_types_and_flags_go_into_the_spec_and_come_back()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        main.Entities.AddExample();
        main.Entities.AddEnum();
        main.Entities.Enums[^1].Name = "Priority";
        main.Entities.Enums[^1].Values = "Low, High";
        var order = main.Entities.Modules[0].Entities.Single(e => e.Name == "Order");
        order.AddProperty();
        var priority = order.Properties[^1];
        priority.Name = "Priority";
        priority.Type = PropertyType.Enum;
        priority.Enum = "Priority";
        priority.Default = "High";
        priority.Facet = true;
        priority.Label = "Urgency";
        priority.Searchable = "No";

        main.Errors.ShouldBeEmpty();
        main.Entities.ReferenceTargets.ShouldBe(["Sales.Customer", "Sales.Order"]);
        main.Entities.EnumNames.ShouldBe(["OrderStatus", "Priority"]);
        var spec = main.ToSpec();
        spec.Enums.Select(e => (e.Name, string.Join(",", e.Values))).ShouldBe([("OrderStatus", "Open,Paid,Shipped"), ("Priority", "Low,High")]);
        var customer = spec.Modules[0].Entities[1].Properties.Single(p => p.Name == "Customer");
        (customer.Type, customer.Reference, customer.Inverse, customer.Required).ShouldBe((PropertyType.Reference, "Customer", "Orders", true));
        var saved = spec.Modules[0].Entities[1].Properties[^1];
        (saved.Enum, saved.Default, saved.Facet, saved.Label, saved.Searchable).ShouldBe(("Priority", "High", true, "Urgency", false));

        main.Load(SpecJson.Read(SpecJson.Write(spec)), null);
        var loaded = main.ToSpec();
        SpecJson.Write(new AppSpec { Name = "Shop", Modules = loaded.Modules, Enums = loaded.Enums }).ShouldBe(SpecJson.Write(new AppSpec { Name = "Shop", Modules = spec.Modules, Enums = spec.Enums }));
        main.Entities.Modules[0].Entities[1].Properties.Single(p => p.Name == "Customer").Summary.ShouldContain("Customer");
    }

    [Fact]
    public void Problems_of_references_and_enums_show_where_they_are_edited()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        main.Entities.AddModule();
        main.Entities.AddEnum();
        main.Entities.Enums[0].Values = string.Empty;
        var entity = main.Entities.SelectedEntity!;
        entity.AddProperty();
        entity.Properties[1].Name = "Owner";
        entity.Properties[1].Type = PropertyType.Reference;
        entity.Properties[1].Reference = "Nobody";

        entity.Errors.ShouldContain(e => e.StartsWith("Property 2 (Owner):", StringComparison.Ordinal) && e.Contains("'Nobody'"));
        main.Entities.Enums[0].Errors.ShouldHaveSingleItem().ShouldContain("at least one value");
    }

    [Fact]
    public void Languages_are_checked_in_the_catalog_order_and_none_means_all()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        var languages = main.Option("languages");

        languages.IsMulti.ShouldBeTrue();
        languages.MultiChoices.Single(c => c.Value == "de").IsChecked = true;
        languages.MultiChoices.Single(c => c.Value == "en").IsChecked = true;
        languages.Value.ShouldBe("en,de");
        main.Option("defaultLanguage").Value = "de";
        main.Errors.ShouldBeEmpty();

        languages.MultiChoices.Single(c => c.Value == "de").IsChecked = false;
        main.Errors.ShouldContain(e => e.StartsWith("defaultLanguage:", StringComparison.Ordinal));
        main.Languages.Errors.ShouldContain(e => e.StartsWith("defaultLanguage:", StringComparison.Ordinal));
        languages.MultiChoices.Single(c => c.Value == "en").IsChecked = false;
        languages.Value.ShouldBeNull();
        main.ToSpec().Options.Keys.ShouldBe(["defaultLanguage"]);
        main.Architecture.Groups.Select(g => g.Name).ShouldContain(OptionCatalog.Tooling);
    }
}

using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;

namespace Coworkee.Studio.Tests;

public sealed class EntityTests
{
    [Fact]
    public void The_designer_becomes_modules_of_the_spec()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        var entities = main.Entities;

        entities.AddModule();
        var module = entities.Modules.ShouldHaveSingleItem();
        module.Name.ShouldBe("Sales");
        var entity = entities.SelectedEntity.ShouldNotBeNull();
        entity.Name = "Invoice";
        entity.ClientEntity = true;
        entity.Mapping = "Mapperly";
        entity.AddProperty();
        entity.Properties[1].Name = "Total";
        entity.Properties[1].Type = PropertyType.Decimal;
        entity.Properties[1].MaxLength = 20;
        entity.Properties[1].Required = true;

        var spec = main.ToSpec().Modules.ShouldHaveSingleItem();
        spec.Name.ShouldBe("Sales");
        var invoice = spec.Entities.ShouldHaveSingleItem();
        invoice.Name.ShouldBe("Invoice");
        invoice.Plural.ShouldBeNull();
        invoice.ClientEntity.ShouldBeTrue();
        invoice.Mapping.ShouldBe(MappingStrategy.Mapperly);
        invoice.Properties[0].ShouldSatisfyAllConditions(p => p.Name.ShouldBe("Name"), p => p.Required.ShouldBeTrue(), p => p.MaxLength.ShouldBe(200));
        invoice.Properties[1].ShouldSatisfyAllConditions(p => p.Type.ShouldBe(PropertyType.Decimal), p => p.MaxLength.ShouldBeNull(), p => p.Required.ShouldBeTrue());
        main.Errors.ShouldBeEmpty();
        SpecOptions.Resolve(main.ToSpec())["clientEntities"].ShouldBe("true");
    }

    [Fact]
    public void A_module_without_entities_is_reported_on_the_module()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);
        main.Basics.Name = "Shop";
        main.Entities.AddModule();
        main.Entities.RemoveEntity(main.Entities.SelectedEntity!);

        main.Entities.Modules[0].Errors.ShouldHaveSingleItem().ShouldBe("a module needs at least one entity.");
    }

    [Fact]
    public void The_preview_names_route_permissions_and_files()
    {
        var preview = EntityPreview.For("Shop", "Sales", new EntitySpec { Name = "OrderItem" });

        preview.Route.ShouldBe("/sales/order-items");
        preview.ApiRoute.ShouldBe("api/v1/order-items");
        preview.Permissions.ShouldBe(["Sales.OrderItems.View", "Sales.OrderItems.Create", "Sales.OrderItems.Edit", "Sales.OrderItems.Delete"]);
        preview.Files.ShouldContain("src/Shop.Web.Client/Pages/Sales/OrderItemsPage.razor");
        preview.Files.ShouldContain("src/Shop.Sales/Domain/OrderItem.cs");
    }

    [Fact]
    public void The_example_adds_the_sales_module_once()
    {
        using var folder = new TempFolder();
        var main = Studio.Create(folder);

        main.Entities.AddExample();
        main.Entities.AddExample();

        main.Entities.Modules.ShouldHaveSingleItem().Entities.Select(e => e.Name).ShouldBe(["Customer", "Order"]);
        main.Entities.SelectedEntity!.IsSelected.ShouldBeTrue();
    }
}

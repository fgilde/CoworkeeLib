using Coworkee.Cli.Spec;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;

namespace Coworkee.Studio.Tests;

public sealed class DiagramTests
{
    [Fact]
    public void Auto_layout_puts_referenced_entities_above_and_the_positions_travel_in_the_spec()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        var sales = main.Entities.Modules[0];

        main.Entities.AutoLayout();

        var customer = sales.Entities.Single(e => e.Name == "Customer").Layout!;
        var order = sales.Entities.Single(e => e.Name == "Order").Layout!;
        customer.Y.ShouldBeLessThan(order.Y);
        main.Errors.ShouldBeEmpty();
        var json = SpecJson.Write(main.ToSpec());
        json.ShouldContain("\"layout\"");
        main.Load(SpecJson.Read(json), null);
        main.Entities.Modules[0].Entities.Single(e => e.Name == "Order").Layout!.X.ShouldBe(order.X);
    }

    [Fact]
    public void Entities_without_a_position_go_below_the_placed_ones()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        var order = main.Entities.Modules[0].Entities.Single(e => e.Name == "Order");
        order.MoveTo(500, 80);

        var positions = DiagramLayout.Positions(main.Entities);

        positions[order].ShouldBe(new Avalonia.Point(500, 80));
        positions[main.Entities.Modules[0].Entities.Single(e => e.Name == "Customer")].Y.ShouldBeGreaterThan(80 + DiagramLayout.Height(order));
    }

    [Fact]
    public void A_line_drawn_between_two_boxes_becomes_a_reference_after_the_dialog()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        var entities = main.Entities;
        var customer = entities.Modules[0].Entities.Single(e => e.Name == "Customer");
        var order = entities.Modules[0].Entities.Single(e => e.Name == "Order");
        entities.AddModule();
        entities.Modules[^1].Name = "Crm";
        var lead = entities.Modules[^1].Entities[0];

        entities.BeginReference(order, customer);
        entities.PendingReference!.Name.ShouldBe("Customer2");
        entities.PendingReference.Name = "Payer";
        entities.PendingReference.Required = true;
        entities.PendingReference.OnDelete = "Cascade";
        entities.ConfirmReference();
        entities.BeginReference(lead, customer);
        entities.ConfirmReference();
        entities.BeginReference(lead, order);
        entities.CancelReference();

        entities.HasPendingReference.ShouldBeFalse();
        var payer = order.Properties[^1].ToSpec();
        (payer.Name, payer.Reference, payer.Required, payer.OnDelete).ShouldBe(("Payer", "Customer", true, DeleteBehavior.Cascade));
        lead.Properties.Select(p => p.Reference).ShouldBe([null, "Sales.Customer"]);
        entities.SelectedEntity.ShouldBe(lead);
        main.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_previews_a_csv_file_and_adds_its_entity_to_the_spec()
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        var import = main.Entities.Import;
        main.Entities.OpenImport();
        import.Kind = "CSV";

        await import.PreviewAsync();
        import.Error.ShouldNotBeNull();

        var file = folder.File("suppliers.csv");
        await File.WriteAllTextAsync(file, "Name,Email,Rating\nAcme,info@acme.test,4\n", TestContext.Current.CancellationToken);
        import.Source = file;
        import.Module = "Sales";
        await import.PreviewAsync();

        import.Error.ShouldBeNull();
        import.Lines.ShouldHaveSingleItem().ShouldStartWith("Supplier (Name: string!");
        import.Add();
        main.Entities.IsImportOpen.ShouldBeFalse();
        main.Entities.Modules.Select(m => m.Name).ShouldBe(["Sales"]);
        main.Entities.SelectedEntity!.Name.ShouldBe("Supplier");
        main.Errors.ShouldBeEmpty();
    }

    [Fact]
    public Task The_diagram_renders_and_exports_in_light_and_dark() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var main = Scenario.Shop(folder);
        main.Entities.ViewMode = "Diagram";
        foreach (var dark in new[] { false, true })
        {
            Render.Step(main, main.Entities, dark).PixelSize.Width.ShouldBe(1400);
        }

        var diagram = new Views.EntityDiagram { Step = main.Entities, Width = 600, Height = 400 };
        using var image = diagram.Export();
        ((double)image.ShouldNotBeNull().PixelSize.Width).ShouldBeGreaterThan(2 * DiagramLayout.Width);
    });
}

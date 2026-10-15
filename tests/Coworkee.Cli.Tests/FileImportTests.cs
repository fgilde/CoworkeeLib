using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;
using Coworkee.Cli.Spec.Import;

namespace Coworkee.Cli.Tests;

public sealed class FileImportTests
{
    public const string OpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Store API", "version": "1" },
          "paths": {},
          "components": {
            "schemas": {
              "OrderStatus": { "type": "string", "enum": ["open", "in_progress", "shipped"] },
              "CustomerDto": {
                "type": "object",
                "required": ["name"],
                "properties": {
                  "id": { "type": "string", "format": "uuid" },
                  "name": { "type": "string", "maxLength": 100 },
                  "email": { "type": "string", "format": "email", "nullable": true },
                  "website": { "type": "string", "format": "uri" }
                }
              },
              "Product": { "type": "object", "properties": { "title": { "type": "string" }, "price": { "type": "number" } } },
              "Order": {
                "type": "object",
                "required": ["number", "customer", "status"],
                "properties": {
                  "id": { "type": "integer" },
                  "number": { "type": "string" },
                  "customer": { "$ref": "#/components/schemas/CustomerDto" },
                  "productId": { "type": "string", "format": "uuid" },
                  "status": { "$ref": "#/components/schemas/OrderStatus" },
                  "placedAt": { "type": "string", "format": "date-time" },
                  "quantity": { "type": "integer", "format": "int64" },
                  "priority": { "type": "string", "enum": ["low", "high"] },
                  "lines": { "type": "array", "items": { "$ref": "#/components/schemas/Product" } },
                  "scan": { "type": "string", "format": "binary" }
                }
              },
              "Ping": { "type": "object" }
            }
          }
        }
        """;

    public const string Csv = """"
        Id;Name;E-Mail;Born;Score;Active;Price;Website;Note
        1;Ada Lovelace;ada@example.com;1815-12-10;42;yes;12.50;https://example.com;"first; with ""quotes"""
        2;Grace Hopper;grace@example.com;1906-12-09;7;no;3.125;https://example.org;
        """";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_openapi_document_becomes_entities_enums_and_references()
    {
        using var folder = new TempFolder();
        var file = folder.File("store.json");
        await File.WriteAllTextAsync(file, OpenApi, Ct);

        var result = await EntityImporter.ImportAsync(new ImportRequest(ImportSource.OpenApi, file), Ct);

        result.Module.Name.ShouldBe("Store");
        result.Module.Entities.Select(e => e.Name).ShouldBe(["Customer", "Product", "Order"]);
        result.Module.Entities[0].Properties.Select(p => (p.Name, p.Type, p.Required, p.MaxLength)).ShouldBe(
            [("Name", PropertyType.String, true, (int?)100), ("Email", PropertyType.Email, false, null), ("Website", PropertyType.Url, false, null)]);
        var order = result.Module.Entities[2].Properties;
        order.Select(p => (p.Name, p.Type, p.Required)).ShouldBe(
        [
            ("Number", PropertyType.String, true), ("Customer", PropertyType.Reference, true), ("Product", PropertyType.Reference, false), ("Status", PropertyType.Enum, true),
            ("PlacedAt", PropertyType.DateTime, false), ("Quantity", PropertyType.Long, false), ("Priority", PropertyType.Enum, false),
        ]);
        order[1].Reference.ShouldBe("Customer");
        order[6].Enum.ShouldBe("OrderPriority");
        result.Enums.Select(e => (e.Name, string.Join(",", e.Values))).ShouldBe([("OrderStatus", "Open,InProgress,Shipped"), ("OrderPriority", "Low,High")]);
        result.Skipped.ShouldContain(s => s.StartsWith("Order.lines: a list", StringComparison.Ordinal));
        result.Skipped.ShouldContain(s => s.StartsWith("Order.scan: string binary", StringComparison.Ordinal));
        result.Skipped.ShouldContain(s => s.StartsWith("Ping:", StringComparison.Ordinal));
        SpecValidator.Validate(Spec(result)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Yaml_and_documents_without_schemas_are_refused_with_a_reason()
    {
        using var folder = new TempFolder();
        var yaml = folder.File("api.yaml");
        await File.WriteAllTextAsync(yaml, "openapi: 3.0.1", Ct);
        var empty = folder.File("empty.json");
        await File.WriteAllTextAsync(empty, """{ "openapi": "3.0.1" }""", Ct);

        (await Should.ThrowAsync<ImportException>(() => EntityImporter.ImportAsync(new ImportRequest(ImportSource.OpenApi, yaml), Ct))).Message.ShouldContain("YAML");
        (await Should.ThrowAsync<ImportException>(() => EntityImporter.ImportAsync(new ImportRequest(ImportSource.OpenApi, empty), Ct))).Message.ShouldContain("components.schemas");
    }

    [Fact]
    public async Task A_csv_file_becomes_one_entity_with_types_inferred_from_its_rows()
    {
        using var folder = new TempFolder();
        var file = folder.File("people.csv");
        await File.WriteAllTextAsync(file, Csv, Ct);

        var result = await EntityImporter.ImportAsync(new ImportRequest(ImportSource.Csv, file, "Crm"), Ct);

        var person = result.Module.Entities.ShouldHaveSingleItem();
        (result.Module.Name, person.Name, person.Plural).ShouldBe(("Crm", "People", null));
        person.Properties.Select(p => (p.Name, p.Type, p.Required)).ShouldBe(
        [
            ("Name", PropertyType.String, true), ("EMail", PropertyType.Email, true), ("Born", PropertyType.DateOnly, true), ("Score", PropertyType.Int, true),
            ("Active", PropertyType.Bool, true), ("Price", PropertyType.Decimal, true), ("Website", PropertyType.Url, true), ("Note", PropertyType.String, false),
        ]);
        person.Properties[0].MaxLength.ShouldBe(50);
        person.Properties[5].Scale.ShouldBe(3);
        result.Skipped.ShouldContain(s => s.StartsWith("Id: left out", StringComparison.Ordinal));
        SpecValidator.Validate(Spec(result)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_spec_out_file_gets_the_module_and_keeps_what_it_had()
    {
        using var folder = new TempFolder();
        var csv = folder.File("suppliers.csv");
        await File.WriteAllTextAsync(csv, "Name,Email\nAda,ada@example.com\n", Ct);
        var spec = Path.Combine(folder.Path, "shop.json");
        await File.WriteAllTextAsync(spec, SpecJson.Write(SpecJson.Example()), Ct);
        var result = await EntityImporter.ImportAsync(new ImportRequest(ImportSource.Csv, csv, "Crm"), Ct);

        var merged = EntityImportCommand.MergeInto(spec, result);
        merged = EntityImportCommand.MergeInto(spec, result);

        merged.Modules.Select(m => m.Name).ShouldBe(["Sales", "Crm"]);
        merged.Modules[1].Entities.ShouldHaveSingleItem().Name.ShouldBe("Supplier");
        SpecValidator.Validate(merged).ShouldBeEmpty();
        EntityImportCommand.MergeInto(Path.Combine(folder.Path, "new.json"), result).Name.ShouldBe("CrmApp");
    }

    [Fact]
    public void Names_become_csharp_identifiers()
    {
        ImportNames.Pascal("order_items").ShouldBe("OrderItems");
        ImportNames.Pascal("ORDER-ITEMS").ShouldBe("OrderItems");
        ImportNames.Pascal("customerID").ShouldBe("CustomerId");
        ImportNames.Pascal("2fa codes").ShouldBe("N2FaCodes");
        ImportNames.Pascal("äöü").ShouldBe("Unnamed");
        ImportNames.Singular("Categories").ShouldBe("Category");
        ImportNames.Singular("Addresses").ShouldBe("Address");
        ImportNames.Singular("Statuses").ShouldBe("Status");
        ImportNames.Singular("Status").ShouldBe("Status");
        ImportNames.TypeName("Task", ImportNames.Set()).ShouldBe("TaskItem");
        var members = ImportNames.Set();
        ImportNames.Member("Customer", "Order", members, reference: true).ShouldBe("Customer");
        ImportNames.Member("CustomerId", "Order", members).ShouldBe("CustomerId2");
        ImportNames.Member("Id", "Order", members).ShouldBe("IdValue");
        ImportNames.Member("Order", "Order", members).ShouldBe("OrderValue");
    }

    internal static AppSpec Spec(ImportResult result)
    {
        var spec = result.ToSpec("Shop");
        spec.Options["samples"] = false;
        return spec;
    }
}

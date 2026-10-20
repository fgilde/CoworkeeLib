using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class RichEntityGenerationTests
{
    private static readonly AppSpec Spec = new()
    {
        Name = "Shop",
        Enums = [new EnumSpec { Name = "OrderStatus", Values = ["Open", "Paid"] }],
        Modules =
        [
            new ModuleSpec
            {
                Name = "Crm",
                Entities = [new EntitySpec { Name = "Customer", Properties = [new PropertySpec { Name = "Code", MaxLength = 20 }, new PropertySpec { Name = "Name", Required = true, Display = true }] }],
            },
            new ModuleSpec
            {
                Name = "Sales",
                Entities =
                [
                    new EntitySpec
                    {
                        Name = "Order",
                        Properties =
                        [
                            new PropertySpec { Name = "Number", Required = true, MaxLength = 20, Unique = true },
                            new PropertySpec { Name = "Customer", Type = PropertyType.Reference, Reference = "Crm.Customer", Required = true, Facet = true, OnDelete = DeleteBehavior.Cascade },
                            new PropertySpec { Name = "Status", Type = PropertyType.Enum, Enum = "OrderStatus", Required = true, Default = "Paid", Facet = true },
                            new PropertySpec { Name = "Total", Type = PropertyType.Money, Required = true, Default = "9.5" },
                            new PropertySpec { Name = "Due", Type = PropertyType.DateOnly, Label = "Due date" },
                            new PropertySpec { Name = "Pickup", Type = PropertyType.TimeOnly },
                            new PropertySpec { Name = "Contact", Type = PropertyType.Email },
                            new PropertySpec { Name = "Site", Type = PropertyType.Url },
                            new PropertySpec { Name = "Note", Type = PropertyType.Text },
                            new PropertySpec { Name = "Weight", Type = PropertyType.Decimal, Precision = 10, Scale = 3, Default = "1" },
                            new PropertySpec { Name = "Gift", Type = PropertyType.Bool, Required = true },
                            new PropertySpec { Name = "Express", Type = PropertyType.Bool },
                        ],
                    },
                    new EntitySpec
                    {
                        Name = "Line",
                        Mapping = MappingStrategy.Mapperly,
                        Properties =
                        [
                            new PropertySpec { Name = "Product", Required = true, MaxLength = 100 },
                            new PropertySpec { Name = "Order", Type = PropertyType.Reference, Reference = "Order", Inverse = "Lines" },
                        ],
                    },
                ],
            },
        ],
    };

    private static List<GeneratedFile> Files(string module, string entity, MappingStrategy mapping = MappingStrategy.Manual) =>
        [.. FeatureWriter.Files(new EntityModel("Shop", module, Spec.Modules.Single(m => m.Name == module).Entities.Single(e => e.Name == entity), mapping, new SpecIndex(Spec)))];

    private static string Content(IEnumerable<GeneratedFile> files, string suffix) => files.Single(f => f.Path.EndsWith(suffix, StringComparison.Ordinal)).Content;

    [Fact]
    public void A_reference_gets_a_foreign_key_navigation_lookup_check_display_record_and_picker()
    {
        var files = Files("Sales", "Order");

        Content(files, "OrderDto.cs").ShouldContain("Guid CustomerId, OrderDto.CustomerReference? Customer, OrderStatus Status, decimal Total, DateOnly? Due, TimeOnly? Pickup");
        Content(files, "OrderDto.cs").ShouldContain("    public sealed record CustomerReference(Guid Id, string? Name);\n}");
        var domain = Content(files, "Domain/Order.cs");
        domain.ShouldContain("using Shop.Crm.Domain;");
        domain.ShouldContain("using Shop.Contracts;");
        domain.ShouldContain("[Nextended.Core.Facets.ProvideFacet(Label = \"Customer\", ValuePath = \"CustomerId\", LabelPath = \"it.Customer.Name\", ValueType = typeof(Guid))]\n    public Guid CustomerId { get; set; }");
        domain.ShouldContain("public Customer? Customer { get; set; }");
        domain.ShouldContain("public OrderStatus Status { get; set; } = OrderStatus.Paid;");
        domain.ShouldContain("public decimal Total { get; set; } = 9.5m;");
        var model = Content(files, "OrderModelContributor.cs");
        model.ShouldContain("entity.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Cascade);");
        model.ShouldContain("entity.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();");
        model.ShouldContain("entity.Property(e => e.Total).HasPrecision(18, 2);");
        model.ShouldContain("entity.Property(e => e.Weight).HasPrecision(10, 3);");
        model.ShouldContain("entity.Property(e => e.Contact).HasMaxLength(256);");
        model.ShouldContain("entity.Property(e => e.Site).HasMaxLength(2000);");
        var handler = Content(files, "AddEditOrder.cs");
        handler.ShouldContain("using Shop.Crm.Domain;");
        handler.ShouldContain("var referencedCustomer = await db.Set<Customer>().SingleOrDefaultAsync(r => r.Id == command.Request.CustomerId, cancellationToken);");
        handler.ShouldContain("return Error.Validation(\"CustomerId\", \"The customer does not exist.\");");
        handler.ShouldContain("entity.Customer = referencedCustomer;");
        handler.ShouldContain("await db.Set<Order>().AnyAsync(e => e.Id != entity.Id && e.Number == entity.Number, cancellationToken)");
        handler.ShouldContain("Error.Conflict(\"sales.order_number_taken\", \"Another order has this number.\")");
        var validator = Content(files, "AddEditOrderValidator.cs");
        validator.ShouldContain("RuleFor(c => c.Request.CustomerId).NotEmpty();");
        validator.ShouldContain("RuleFor(c => c.Request.Status).IsInEnum();");
        validator.ShouldContain("RuleFor(c => c.Request.Contact).MaximumLength(256).EmailAddress().When(c => !string.IsNullOrEmpty(c.Request.Contact));");
        validator.ShouldContain("RuleFor(c => c.Request.Site).MaximumLength(2000).Must(BeAWebAddress)");
        validator.ShouldContain("private static bool BeAWebAddress(string? value)");
        Content(files, "OrderMapping.cs").ShouldContain("entity.CustomerId, entity.Customer is null ? null : new OrderDto.CustomerReference(entity.Customer.Id, entity.Customer.Name), entity.Status");
        Content(files, "AddEditOrderRequest.cs").ShouldContain("public OrderStatus Status { get; set; } = OrderStatus.Paid;");
        var page = Content(files, "OrdersPage.razor");
        page.ShouldContain("EntitySet=\"Orders\" Expand=\"Customer\"");
        page.ShouldContain("<PropertyColumn Property=\"e => e.Customer!.Name\" Title=\"@L[\"Customer\"]\" />");
        page.ShouldContain("<PropertyColumn Property=\"e => e.Total\" Title=\"@L[\"Total\"]\" Format=\"N2\" />");
        page.ShouldContain("Title=\"@L[\"Due date\"]\">\n            <CellTemplate>@(context.Item.Due?.ToString(\"d\"))</CellTemplate>");
        page.ShouldContain("<CellTemplate>@L[Coworkee.Contracts.Data.EnumLabels.Of(context.Item.Status)]</CellTemplate>");
        page.ShouldContain("<CellTemplate>@L[context.Item.Gift ? \"Yes\" : \"No\"]</CellTemplate>");
        page.ShouldContain("<CellTemplate>@(context.Item.Express is { } flag ? L[flag ? \"Yes\" : \"No\"] : null)</CellTemplate>");
        page.ShouldNotContain("e.Note");
        var code = Content(files, "OrdersPage.razor.cs");
        code.ShouldContain("SearchFields = [nameof(OrderDto.Number), nameof(OrderDto.Contact), nameof(OrderDto.Site), nameof(OrderDto.Note)];");
        code.ShouldContain("CustomerId = item.CustomerId");
        code.ShouldContain(".RenderWith<ODataPicker, Guid>(p => p.Value)");
        code.ShouldContain(".WithAdditionalAttribute(nameof(ODataPicker.EntitySet), \"Customers\")");
        code.ShouldContain(".WithAdditionalAttribute(nameof(ODataPicker.TextProperty), \"Name\")");
        code.ShouldContain("meta.Property(p => p.Note).WithLabel(L[\"Note\"]).WithOrder(8).WithAdditionalAttribute(\"Lines\", 4)");
        files.ShouldAllBe(f => !f.Content.Contains("{{") && !f.Content.Contains("}}"));
    }

    [Fact]
    public void Optional_references_inverse_collections_enums_and_module_references_are_generated()
    {
        var lines = Files("Sales", "Line", MappingStrategy.Mapperly);
        Content(lines, "Domain/Line.cs").ShouldContain("public Guid? OrderId { get; set; }");
        Content(lines, "LineModelContributor.cs").ShouldContain("entity.HasOne(e => e.Order).WithMany(t => t.Lines).HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);");
        Content(lines, "AddEditLine.cs").ShouldContain("command.Request.OrderId is { } orderId ? await db.Set<Order>().SingleOrDefaultAsync(r => r.Id == orderId, cancellationToken) : null;");
        Content(lines, "LinesPage.razor.cs").ShouldContain(".RenderWith<OptionalODataPicker, Guid?>(p => p.Value)");
        Content(lines, "LinesPage.razor.cs").ShouldContain(".WithAdditionalAttribute(nameof(OptionalODataPicker.TextProperty), \"Number\")");
        Content(lines, "LineDto.cs").ShouldContain("public sealed record OrderReference(Guid Id, string? Number);");
        Content(lines, "Domain/Line.cs").ShouldNotContain("using Shop.Contracts;");

        Content(Files("Sales", "Order"), "Domain/Order.cs").ShouldContain("public List<Line> Lines { get; set; } = [];");
        var index = new SpecIndex(Spec);
        var module = ServerFiles.Module("Shop", "Sales", [.. Spec.Modules[1].Entities.Select(e => new EntityModel("Shop", "Sales", e, MappingStrategy.Manual, index))]).ToList();
        Content(module, "Shop.Sales.csproj").ShouldContain("<ProjectReference Include=\"..\\Shop.Crm\\Shop.Crm.csproj\" />");
        var status = ServerFiles.Enum("Shop", Spec.Enums[0]);
        status.Path.ShouldBe("src/Shop.Contracts/Enums/OrderStatus.cs");
        status.Content.ShouldContain("[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]\npublic enum OrderStatus\n{\n    Open,\n    Paid,\n}");
    }
}

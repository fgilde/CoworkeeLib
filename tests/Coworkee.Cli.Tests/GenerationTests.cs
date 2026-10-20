using Coworkee.Cli.Commands;
using Coworkee.Cli.Generation;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class GenerationTests
{
    private static readonly EntitySpec Order = new()
    {
        Name = "Order",
        ClientEntity = true,
        Properties =
        [
            new PropertySpec { Name = "Number", Required = true, MaxLength = 50 },
            new PropertySpec { Name = "Note" },
            new PropertySpec { Name = "Total", Type = PropertyType.Decimal, Required = true },
            new PropertySpec { Name = "ShippedAt", Type = PropertyType.DateTime },
        ],
    };

    private static EntityModel Model(EntitySpec spec, MappingStrategy mapping = MappingStrategy.Manual) => new("Shop", "Sales", spec, mapping);

    private static string Content(IEnumerable<GeneratedFile> files, string suffix) => files.Single(f => f.Path.EndsWith(suffix, StringComparison.Ordinal)).Content;

    [Fact]
    public void An_entity_gets_contracts_domain_persistence_permissions_features_endpoints_and_client_files()
    {
        var files = FeatureWriter.Files(Model(Order)).ToList();

        files.Select(f => f.Path).ShouldBe(
        [
            "src/Shop.Contracts/Sales/SalesPermissions.Orders.cs",
            "src/Shop.Contracts/Sales/OrderDto.cs",
            "src/Shop.Contracts/Sales/AddEditOrderRequest.cs",
            "src/Shop.Sales/Domain/Order.cs",
            "src/Shop.Sales/Persistence/OrderModelContributor.cs",
            "src/Shop.Sales/Permissions/OrderPermissionDefinitions.cs",
            "src/Shop.Sales/Features/Orders/AddEditOrder.cs",
            "src/Shop.Sales/Features/Orders/AddEditOrderValidator.cs",
            "src/Shop.Sales/Features/Orders/DeleteOrders.cs",
            "src/Shop.Sales/Features/Orders/OrderMapping.cs",
            "src/Shop.Sales/Endpoints/OrderEndpoints.cs",
            "src/Shop.Web.Client/Pages/Sales/OrdersPage.razor",
            "src/Shop.Web.Client/Pages/Sales/OrdersPage.razor.cs",
            "src/Shop.Web.Client/Api/IOrdersApi.cs",
            "src/Shop.Web.Client/Api/OrdersApi.cs",
            "src/Shop.Web.Client/Navigation/OrdersNavigation.cs",
        ]);
        Content(files, "OrderDto.cs").ShouldContain("public sealed record OrderDto(Guid Id, string Number, string? Note, decimal Total, DateTimeOffset? ShippedAt);");
        Content(files, "Domain/Order.cs").ShouldContain("[ClientEntity]");
        Content(files, "Domain/Order.cs").ShouldContain("[Realtime(SalesPermissions.Orders.View)]");
        Content(files, "Domain/Order.cs").ShouldContain("public string Number { get; set; } = string.Empty;");
        Content(files, "Domain/Order.cs").ShouldContain("public DateTimeOffset? ShippedAt { get; set; }");
        Content(files, "OrderModelContributor.cs").ShouldContain("entity.ToTable(\"Orders\", \"app\");");
        Content(files, "OrderModelContributor.cs").ShouldContain("entity.Property(e => e.Number).HasMaxLength(50);");
        Content(files, "AddEditOrderValidator.cs").ShouldContain("RuleFor(c => c.Request.Number).NotEmpty().MaximumLength(50);");
        Content(files, "AddEditOrderValidator.cs").ShouldNotContain("Total");
        Content(files, "AddEditOrder.cs").ShouldContain("entity.Number = command.Request.Number.Trim();");
        Content(files, "AddEditOrder.cs").ShouldContain("entity.Note = command.Request.Note?.Trim();");
        Content(files, "AddEditOrder.cs").ShouldContain("entity.Total = command.Request.Total;");
        Content(files, "OrderMapping.cs").ShouldContain("new(entity.Id, entity.Number, entity.Note, entity.Total, entity.ShippedAt)");
        Content(files, "OrderEndpoints.cs").ShouldContain("app.MapCoworkeeApi(\"/api/v1/orders\")");
        Content(files, "SalesPermissions.Orders.cs").ShouldContain("public const string Delete = \"Sales.Orders.Delete\";");
        Content(files, "OrdersPage.razor").ShouldContain("@page \"/sales/orders\"");
        Content(files, "OrdersPage.razor").ShouldNotContain("ToolBarContent", customMessage: "the table brings the client entity switch itself");
        Content(files, "OrdersPage.razor").ShouldContain("<PageHeader Title=\"Orders\" Description=\"Manage orders.\" />", customMessage: "PageHeader translates its texts, the page passes keys");
        Content(files, "OrdersPage.razor").ShouldContain("CreateText=\"New order\"");
        Content(files, "OrdersPage.razor").ShouldContain("<PropertyColumn Property=\"e => e.ShippedAt\" Title=\"@L[\"Shipped at\"]\" />");
        Content(files, "OrdersPage.razor.cs").ShouldContain("[nameof(OrderDto.Number), nameof(OrderDto.Note)]");
        Content(files, "OrdersNavigation.cs").ShouldContain("Group: \"Sales\"");
        files.ShouldAllBe(f => !f.Content.Contains("{{") && !f.Content.Contains("}}"));
    }

    [Fact]
    public void Mapperly_entities_get_a_mapper_and_entities_without_rules_no_validator()
    {
        var spec = new EntitySpec { Name = "Tag", Properties = [new PropertySpec { Name = "Weight", Type = PropertyType.Int }] };
        var files = FeatureWriter.Files(Model(spec, MappingStrategy.Mapperly)).ToList();

        Content(files, "TagMapping.cs").ShouldContain("[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]");
        Content(files, "TagMapping.cs").ShouldContain("public static partial TagDto ToDto(this Tag entity);");
        files.ShouldNotContain(f => f.Path.EndsWith("Validator.cs", StringComparison.Ordinal));
        Content(files, "Domain/Tag.cs").ShouldNotContain("[ClientEntity]");
        Content(files, "TagsPage.razor").ShouldNotContain("ClientEntityIndicator");
        Content(files, "TagsPage.razor.cs").ShouldContain("SearchFields = [];");
    }

    [Fact]
    public void The_module_registers_its_entities_and_references_mapperly_when_needed()
    {
        var files = ServerFiles.Module("Shop", "Sales", [Model(Order), Model(new EntitySpec { Name = "Customer", Properties = [new PropertySpec { Name = "Name" }] }, MappingStrategy.Mapperly)]).ToList();

        var module = Content(files, "ShopSalesModule.cs");
        module.ShouldContain("context.Services.AddODataEntity<Order>(\"Orders\", SalesPermissions.Orders.View);");
        module.ShouldContain("context.Services.AddSingleton<IModelContributor, CustomerModelContributor>();");
        module.ShouldContain("using Coworkee.OData.GlobalSearch;");
        module.ShouldContain("context.Services.AddGlobalSearch<Order>(s => s.Fields(e => e.Number, e => e.Note).Page(\"/sales/orders\"));");
        ServerFiles.Services(Model(new EntitySpec { Name = "Tag", Properties = [new PropertySpec { Name = "Weight", Type = PropertyType.Int }] })).ShouldNotContain("AddGlobalSearch");
        module.ShouldContain("        app.MapOrderEndpoints();\n        app.MapCustomerEndpoints();\n    }");
        Content(files, "Shop.Sales.csproj").ShouldContain("Riok.Mapperly");
        Content(ServerFiles.Module("Shop", "Sales", [Model(Order)]), "Shop.Sales.csproj").ShouldNotContain("Riok.Mapperly");
    }

    [Fact]
    public void App_edits_wire_the_module_into_the_solution_and_leave_existing_wiring_alone()
    {
        var slnx = "<Solution>\n  <Folder Name=\"/src/\">\n    <Project Path=\"src/Shop.Api/Shop.Api.csproj\" />\n  </Folder>\n  <Folder Name=\"/tests/\">\n  </Folder>\n</Solution>\n";
        var added = AppEdits.AddToSolution(slnx, "src/Shop.Sales/Shop.Sales.csproj");
        added.ShouldContain("    <Project Path=\"src/Shop.Api/Shop.Api.csproj\" />\n    <Project Path=\"src/Shop.Sales/Shop.Sales.csproj\" />\n  </Folder>\n  <Folder Name=\"/tests/\">");
        AppEdits.AddToSolution(added, "src/Shop.Sales/Shop.Sales.csproj").ShouldBe(added);

        var project = "<Project>\n  <ItemGroup>\n    <A />\n  </ItemGroup>\n</Project>\n";
        var reference = "    <ProjectReference Include=\"..\\Shop.Sales\\Shop.Sales.csproj\" />\n";
        AppEdits.AddToItems(project, reference).ShouldContain("    <A />\n" + reference + "  </ItemGroup>");
        AppEdits.AddToItems(AppEdits.AddToItems(project, reference), reference).ShouldBe(AppEdits.AddToItems(project, reference));

        AppEdits.RegisterModule("[DependsOn(typeof(A))]\npublic sealed class ShopDatabaseModule;", "Shop.Sales.ShopSalesModule")
            .ShouldStartWith("[DependsOn(typeof(Shop.Sales.ShopSalesModule), typeof(A))]");
        Should.Throw<CliException>(() => AppEdits.RegisterModule("public sealed class ShopDatabaseModule;", "X"));

        var program = "using A;\nvar builder = 1;\nvar host = builder.Build();\n";
        AppEdits.AddBeforeBuild(program, "x();\n").ShouldBe("using A;\nvar builder = 1;\nx();\nvar host = builder.Build();\n");
        var enabled = AppEdits.EnableClientEntities(program);
        enabled.ShouldStartWith("using Coworkee.Client.Blazor.ClientEntities;\n");
        enabled.ShouldContain("builder.Services.AddCoworkeeClientEntities();\nvar host");
        AppEdits.EnableClientEntities(enabled).ShouldBe(enabled);
        AppEdits.Normalize("a\r\nb").ShouldBe("a\nb");
    }

    [Fact]
    public void Entity_add_extends_a_generated_module_class()
    {
        var module = Content(ServerFiles.Module("Shop", "Sales", [Model(Order)]), "ShopSalesModule.cs");
        var invoice = Model(new EntitySpec { Name = "Invoice", Properties = [new PropertySpec { Name = "Number" }] });

        var extended = AppEdits.AddToModule(module, ServerFiles.Services(invoice), ServerFiles.Endpoint(invoice));

        extended.ShouldContain(".Page(\"/sales/orders\"));\n        context.Services.AddSingleton<IModelContributor, InvoiceModelContributor>();");
        extended.ShouldContain("context.Services.AddGlobalSearch<Invoice>(s => s.Fields(e => e.Number).Page(\"/sales/invoices\"));");
        extended.ShouldContain("    {\n        app.MapInvoiceEndpoints();\n        app.MapOrderEndpoints();");
        AppEdits.AddToModule(module.Replace("using Coworkee.OData.GlobalSearch;\n", string.Empty), ServerFiles.Services(invoice), ServerFiles.Endpoint(invoice))
            .ShouldContain("using Coworkee.OData.GlobalSearch;\nusing Microsoft.");
        Should.Throw<CliException>(() => AppEdits.AddToModule("class X;", "a", "b"));
    }

    [Fact]
    public async Task Module_and_entity_add_write_into_an_app_and_refuse_to_overwrite()
    {
        using var app = MinimalApp();
        var solution = Solution.Find(app.Path);

        (await ModuleCommand.AddAsync(solution, ModuleCommand.Read(CliApp.Build().Parse("module add Orders --property Number:string:required:20 --property Total:decimal --mapping Mapperly --client-entity")), TestContext.Current.CancellationToken)).ShouldBe(0);
        var code = FeatureOptions.Property("Code:string:required:10");
        (await EntityCommand.AddAsync(solution, "Orders", new EntitySpec { Name = "Coupon", Properties = [code] }, TestContext.Current.CancellationToken)).ShouldBe(0);

        File.ReadAllText(Path.Combine(app.Path, "Shop.slnx")).ShouldContain("src/Shop.Orders/Shop.Orders.csproj");
        File.ReadAllText(Path.Combine(app.Path, "src/Shop.Infrastructure/Shop.Infrastructure.csproj")).ShouldContain("..\\Shop.Orders\\Shop.Orders.csproj");
        File.ReadAllText(Path.Combine(app.Path, "src/Shop.Infrastructure/ShopDatabaseModule.cs")).ShouldContain("typeof(Shop.Orders.ShopOrdersModule)");
        var program = File.ReadAllText(Path.Combine(app.Path, "src/Shop.Web.Client/Program.cs"));
        program.ShouldContain("Shop.Web.Client.Api.IOrdersApi");
        program.ShouldContain("Shop.Web.Client.Navigation.CouponsNavigation");
        program.ShouldContain("AddCoworkeeClientEntities");
        File.ReadAllText(Path.Combine(app.Path, "Directory.Packages.props")).ShouldContain("Riok.Mapperly");
        File.ReadAllText(Path.Combine(app.Path, "src/Shop.Web.Client/Shop.Web.Client.csproj")).ShouldContain("Coworkee.Client.Blazor.ClientEntities");
        var module = File.ReadAllText(Path.Combine(app.Path, "src/Shop.Orders/ShopOrdersModule.cs"));
        module.ShouldContain("AddODataEntity<Coupon>(\"Coupons\"");
        module.ShouldContain("app.MapCouponEndpoints();");
        File.ReadAllText(Path.Combine(app.Path, "src/Shop.Orders/Domain/Order.cs")).ShouldContain("public decimal? Total { get; set; }");

        await Should.ThrowAsync<CliException>(() => EntityCommand.AddAsync(solution, "Orders", new EntitySpec { Name = "Coupon", Properties = [code] }, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<CliException>(() => EntityCommand.AddAsync(solution, "Billing", new EntitySpec { Name = "Bill", Properties = [code] }, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<CliException>(() => ModuleCommand.AddAsync(solution, new ModuleSpec { Name = "Api", Entities = [Order] }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failing_edit_leaves_the_app_untouched()
    {
        using var app = MinimalApp();
        File.WriteAllText(Path.Combine(app.Path, "src/Shop.Infrastructure/ShopDatabaseModule.cs"), "public sealed class ShopDatabaseModule;\n");
        var slnx = File.ReadAllText(Path.Combine(app.Path, "Shop.slnx"));

        await Should.ThrowAsync<CliException>(() => ModuleCommand.AddAsync(Solution.Find(app.Path), ModuleCommand.Read(CliApp.Build().Parse("module add Orders --client-entity")), TestContext.Current.CancellationToken));

        Directory.Exists(Path.Combine(app.Path, "src/Shop.Orders")).ShouldBeFalse();
        Directory.Exists(Path.Combine(app.Path, "src/Shop.Contracts")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(app.Path, "Shop.slnx")).ShouldBe(slnx);
        File.ReadAllText(Path.Combine(app.Path, "src/Shop.Web.Client/Program.cs")).ShouldNotContain("Orders");
    }

    [Theory]
    [InlineData("Number", "Number", PropertyType.String, false, null)]
    [InlineData("Total:decimal:required", "Total", PropertyType.Decimal, true, null)]
    [InlineData("Code:required:10", "Code", PropertyType.String, true, 10)]
    [InlineData("At:DATETIME", "At", PropertyType.DateTime, false, null)]
    public void Properties_parse_from_the_command_line(string text, string name, PropertyType type, bool required, int? maxLength)
    {
        var property = FeatureOptions.Property(text);

        (property.Name, property.Type, property.Required, property.MaxLength).ShouldBe((name, type, required, maxLength));
        Should.Throw<CliException>(() => FeatureOptions.Property("X:huge"));
    }

    private static TempFolder MinimalApp()
    {
        var app = new TempFolder();
        File.WriteAllText(app.File("Shop.slnx"), "<Solution>\n  <Folder Name=\"/src/\">\n  </Folder>\n</Solution>\n");
        File.WriteAllText(app.File("Directory.Packages.props"), "<Project>\n  <ItemGroup>\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(app.File("src/Shop.Infrastructure/Shop.Infrastructure.csproj"), "<Project>\n  <ItemGroup>\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(app.File("src/Shop.Infrastructure/ShopDatabaseModule.cs"), "[DependsOn(typeof(A))]\npublic sealed class ShopDatabaseModule;\n");
        File.WriteAllText(app.File("src/Shop.Web.Client/Shop.Web.Client.csproj"), "<Project>\r\n  <ItemGroup>\r\n  </ItemGroup>\r\n</Project>\r\n");
        File.WriteAllText(app.File("src/Shop.Web.Client/Program.cs"), "var builder = 1;\nvar host = builder.Build();\n");
        return app;
    }
}

using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Components.Query;
using Coworkee.Contracts.Query;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class QueryBuilderTests : ClientTestBase
{
    private static readonly QueryField[] Fields =
    [
        new("Name", "Name", QueryFieldType.Text),
        new("Price", "Price", QueryFieldType.Number),
        new("Kind", "Kind", QueryFieldType.Enum) { Options = [new("Hand", "Hand tool"), new("Power", "Power tool")] },
    ];

    [Fact]
    public async Task Rules_and_groups_are_added_edited_moved_and_removed()
    {
        var popovers = Render<MudPopoverProvider>();
        var query = new QueryGroup();
        var changes = 0;
        var builder = Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query).Add(b => b.ValueChanged, _ => changes++));

        await builder.Find("[data-testid='query-add-rule']").ClickAsync(new());
        await builder.Find("[data-testid='query-value'] input, input[data-testid='query-value']").InputAsync(new ChangeEventArgs { Value = "dri" });
        builder.WaitForAssertion(() => ODataQueryTranslator.ToFilter(query, Fields).ShouldBe("contains(tolower(Name),'dri')"));

        await builder.Find("[data-testid='query-add-group']").ClickAsync(new());
        var nested = (QueryGroup)query.Children[1];
        nested.Logic.ShouldBe(QueryLogic.Or);
        ((QueryRule)nested.Children[0]).Operator = QueryOperator.LessThan;
        ((QueryRule)nested.Children[0]).Field = "Price";
        ((QueryRule)nested.Children[0]).Value = "5";
        await builder.Find("[data-depth='0'] input[data-testid='query-not'], [data-depth='0'] [data-testid='query-not'] input").ChangeAsync(new ChangeEventArgs { Value = true });
        ODataQueryTranslator.ToFilter(query, Fields).ShouldBe("not (contains(tolower(Name),'dri') and Price lt 5)");

        await builder.FindAll("[data-testid='query-move-down']")[0].ClickAsync(new());
        query.Children[0].ShouldBeOfType<QueryGroup>();
        await builder.FindAll("[data-testid='query-remove-group']")[0].ClickAsync(new());
        query.Children.Count.ShouldBe(1);
        await builder.Find("[data-testid='query-remove-rule']").ClickAsync(new());
        query.IsEmpty.ShouldBeTrue();
        changes.ShouldBeGreaterThan(5);
        popovers.Markup.ShouldNotBeNull();
    }

    [Fact]
    public void The_field_and_operator_pickers_reset_values_that_no_longer_fit()
    {
        var popovers = Render<MudPopoverProvider>();
        var query = new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.Contains, Value = "x" }] };
        var builder = Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query));

        builder.Find("[data-field='Name'] .qb-field .mud-input-control").MouseDown();
        popovers.WaitForElement(".mud-list-item:nth-of-type(3), .mud-list-item").ShouldNotBeNull();
        popovers.FindAll(".mud-list-item").Single(i => i.TextContent.Contains("Kind")).Click();

        builder.WaitForAssertion(() => ((QueryRule)query.Children[0]).Field.ShouldBe("Kind"));
        var rule = (QueryRule)query.Children[0];
        (rule.Operator, rule.Value).ShouldBe((QueryOperator.In, null));
        builder.Find("[data-testid='query-values']").ShouldNotBeNull();
    }

    [Fact]
    public async Task The_preview_reads_as_text_odata_and_json_and_json_imports()
    {
        var query = new QueryGroup { Children = [new QueryRule { Field = "Kind", Operator = QueryOperator.In, Values = ["Hand", "Power"] }] };
        var builder = Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query));

        builder.Find("[data-testid='query-preview-text']").TextContent.ShouldBe("Kind is any of Hand tool, Power tool");
        await builder.FindAll("[data-testid='query-preview-mode'] .mud-toggle-item")[1].ClickAsync(new());
        builder.Find("[data-testid='query-preview-text']").TextContent.ShouldBe("(Kind eq 'Hand' or Kind eq 'Power')");
        builder.Find("[data-testid='query-export']").GetAttribute("href")!.ShouldStartWith("data:application/json");

        await builder.Find("[data-testid='query-import']").ClickAsync(new());
        await builder.Find("textarea[data-testid='query-import-text'], [data-testid='query-import-text'] textarea").ChangeAsync(new ChangeEventArgs { Value = """{"logic":"Or","children":[{"kind":"rule","field":"Price","operator":"GreaterThan","value":"10"}]}""" });
        await builder.Find("[data-testid='query-import-apply']").ClickAsync(new());

        query.Logic.ShouldBe(QueryLogic.Or);
        ODataQueryTranslator.ToFilter(query, Fields).ShouldBe("Price gt 10");
        await builder.Find("[data-testid='query-import']").ClickAsync(new());
        await builder.Find("textarea[data-testid='query-import-text'], [data-testid='query-import-text'] textarea").ChangeAsync(new ChangeEventArgs { Value = "nope" });
        await builder.Find("[data-testid='query-import-apply']").ClickAsync(new());
        builder.Markup.ShouldContain("This is not a query.");
    }

    [Fact]
    public void An_enum_the_client_marks_as_flags_compares_as_the_server_s_facet_says()
    {
        var l = Services.GetRequiredService<Localization.CoworkeeLocalizer>();
        var rule = new QueryGroup { Children = [new QueryRule { Field = "State", Operator = QueryOperator.In, Values = ["Draft", "Booked"] }] };
        FacetGroupDto Facet(FacetValueKind kind) => new("state", "State", "State", FacetKind.CheckboxList, FacetOperator.Or, [], true, 0) { ValueKind = kind };

        // a plain enum on the server (stored as text there): "has" failed, the rules compare by name and the zero member is a choice
        var plain = DataTableQueryFields.Field("State", "State", typeof(ShipmentState), l, [Facet(FacetValueKind.Enum)])!;
        plain.Flags.ShouldBeFalse();
        plain.Options!.Select(o => o.Value).ShouldContain("Draft");
        ODataQueryTranslator.ToFilter(rule, [plain]).ShouldBe("(State eq 'Draft' or State eq 'Booked')");

        // flags on the server too, or no facet to tell: the client's type decides
        DataTableQueryFields.Field("State", "State", typeof(ShipmentState), l, [Facet(FacetValueKind.Flags)])!.Flags.ShouldBeTrue();
        var flags = DataTableQueryFields.Field("State", "State", typeof(ShipmentState), l)!;
        flags.Flags.ShouldBeTrue();
        ODataQueryTranslator.ToFilter(new QueryGroup { Children = [new QueryRule { Field = "State", Operator = QueryOperator.In, Values = ["Booked"] }] }, [flags])
            .ShouldBe("State has 'Booked'");
    }

    [Flags]
    public enum ShipmentState
    {
        Draft = 0,
        Booked = 1,
        InTransit = 8,
    }

    [Fact]
    public void The_table_filters_with_the_advanced_filter_and_keeps_it_in_its_state()
    {
        var odata = new FakeODataClient().With("Gadgets", new DataTableTests.Gadget(Guid.NewGuid(), "Drill", "Tools"));
        ODataQuery? last = null;
        odata.Queried += (_, q) => last = q.Facets ? q : last;
        Services.AddSingleton<IODataClient>(odata);
        AddAuthorization().SetAuthorized("Ada");
        Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<DataTableTests.Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, (RenderFragment)(b =>
        {
            b.OpenComponent<PropertyColumn<DataTableTests.Gadget, string>>(0);
            b.AddComponentParameter(1, "Property", (System.Linq.Expressions.Expression<Func<DataTableTests.Gadget, string>>)(g => g.Name));
            b.CloseComponent();
        })));
        table.WaitForAssertion(() => last.ShouldNotBeNull());

        table.Find("[data-testid='advanced-filter']").Click();
        table.Find("[data-testid='query-add-rule']").Click();
        table.Find("[data-testid='advanced-panel'] [data-testid='query-value'] input, [data-testid='advanced-panel'] input[data-testid='query-value']").Input("dri");

        table.WaitForAssertion(() => last!.Filter.ShouldBe("contains(tolower(Name),'dri')"));
        table.WaitForAssertion(() => table.Find("[data-filter-chip='advanced']").TextContent.ShouldContain("Name contains \"dri\""));
        var state = table.Instance.CurrentState;
        DataTableState.Decode(state.Encode())!.Advanced.ShouldNotBeNull();
        ODataQueryTranslator.ToFilter(DataTableState.Decode(state.Encode())!.Advanced!, [new("Name", "Name", QueryFieldType.Text)]).ShouldBe("contains(tolower(Name),'dri')");

        table.Find("[data-filter-chip='advanced'] .mud-chip-close-button").Click();
        table.WaitForAssertion(() => last!.Filter.ShouldBeNull());
    }

    [Fact]
    public void The_table_sends_the_facet_choice_apart_and_shows_enum_labels()
    {
        var status = new FacetGroupDto("status", "Status", "Status", FacetKind.CheckboxList, FacetOperator.Or,
            [new(System.Text.Json.JsonSerializer.SerializeToElement("Shipped"), "On the way", true, 2, "Status eq 'Shipped'")], true, 0) { ValueKind = FacetValueKind.Enum };
        var odata = new FakeODataClient().With("Gadgets", new DataTableTests.Gadget(Guid.NewGuid(), "Drill", "Tools")).WithFacets(status);
        ODataQuery? last = null;
        odata.Queried += (_, q) => last = q.Facets ? q : last;
        Services.AddSingleton<IODataClient>(odata);
        AddAuthorization().SetAuthorized("Ada");
        var popovers = Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<DataTableTests.Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.SearchFields, ["Name"]));
        table.WaitForAssertion(() => table.Find("[data-facet='status']"));

        // the facets render again after the rows: click what the last render shows
        table.WaitForAssertion(() => table.Find("[data-facet='status'] [data-testid='facet-button']").Click());
        popovers.WaitForElement("[data-option=\"Status eq 'Shipped'\"]").TextContent.ShouldContain("On the way");
        popovers.WaitForAssertion(() => popovers.Find("[data-option=\"Status eq 'Shipped'\"]").Click());

        table.WaitForAssertion(() => last!.FacetContext!.Groups["status"].ShouldBe("Status eq 'Shipped'"));
        last!.FacetContext!.Base.ShouldBeNull();
        table.WaitForAssertion(() => table.Find("[data-testid='facet-chips']").TextContent.ShouldContain("On the way"));
    }

    private static QueryRule R(string value) => new() { Field = "Name", Operator = QueryOperator.Contains, Value = value };

    private static string Text(QueryGroup g) => ODataQueryTranslator.ToFilter(g, Fields)!;

    private (IRenderedComponent<MudPopoverProvider> Popovers, IRenderedComponent<QueryBuilder> Builder) Build(QueryGroup query, int maxDepth = 3)
    {
        var popovers = Render<MudPopoverProvider>();
        return (popovers, Render<QueryBuilder>(p => p.Add(b => b.Fields, Fields).Add(b => b.Value, query).Add(b => b.MaxDepth, maxDepth)));
    }

    private static async Task Menu(IRenderedComponent<MudPopoverProvider> popovers, IRenderedComponent<QueryBuilder> builder, string rowSelector, int row, string item)
    {
        await builder.FindAll(rowSelector)[row].QuerySelector("[data-testid='query-node-menu'] button, button[data-testid='query-node-menu']")!.ClickAsync(new());
        await popovers.WaitForElement($"[data-testid='{item}']").ClickAsync(new());
    }

    [Fact]
    public async Task A_rule_moves_up_down_and_into_another_group()
    {
        var inner = new QueryGroup { Logic = QueryLogic.Or, Children = [R("c")] };
        var query = new QueryGroup { Children = [R("a"), R("b"), inner] };
        var (popovers, builder) = Build(query);

        await builder.FindAll("[data-testid='query-rule'] [data-testid='query-move-down']")[0].ClickAsync(new());
        query.Children.Take(2).Cast<QueryRule>().Select(r => r.Value).ShouldBe(["b", "a"]);
        await builder.FindAll("[data-testid='query-rule'] [data-testid='query-move-up']")[1].ClickAsync(new());
        query.Children.Take(2).Cast<QueryRule>().Select(r => r.Value).ShouldBe(["a", "b"]);

        await Menu(popovers, builder, "[data-testid='query-rule']", 0, "query-move-into");
        query.Children.Count.ShouldBe(2);
        inner.Children.Count.ShouldBe(2);
        ((QueryRule)inner.Children[1]).Value.ShouldBe("a");

        await Menu(popovers, builder, "[data-testid='query-rule']", 2, "query-move-out");
        query.Children.OfType<QueryRule>().Select(r => r.Value).ShouldBe(["b", "a"]);

        // drag and drop: the rule dropped on another goes in front of it
        await builder.FindAll("[data-testid='query-rule']")[2].QuerySelector("[data-testid='query-drag']")!.TriggerEventAsync("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        await builder.FindAll("[data-testid='query-rule']")[0].TriggerEventAsync("ondrop", new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        query.Children.OfType<QueryRule>().Select(r => r.Value).ShouldBe(["a", "b"]);
        query.Children.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_group_moves_with_its_rules()
    {
        var inner = new QueryGroup { Logic = QueryLogic.Or, Children = [R("c"), R("d")] };
        var other = new QueryGroup { Children = [R("e")] };
        var query = new QueryGroup { Children = [R("a"), inner, other] };
        var (popovers, builder) = Build(query);

        const string heads = "[data-testid='query-group'][data-depth='1'] > .qb-group-head";
        await builder.FindAll(heads + " [data-testid='query-move-down']")[0].ClickAsync(new());
        query.Children[2].ShouldBe(inner);
        await builder.FindAll(heads + " [data-testid='query-move-up']")[1].ClickAsync(new());
        query.Children[1].ShouldBe(inner);

        // into the other group, rules and all
        await Menu(popovers, builder, heads, 0, "query-move-into");
        query.Children.Count.ShouldBe(2);
        other.Children.ShouldContain(inner);
        inner.Children.Count.ShouldBe(2);
        Text(query).ShouldBe("(contains(tolower(Name),'a') and (contains(tolower(Name),'e') and (contains(tolower(Name),'c') or contains(tolower(Name),'d'))))");
    }

    [Fact]
    public async Task AND_OR_of_a_group_changes_afterwards()
    {
        var inner = new QueryGroup { Logic = QueryLogic.And, Children = [R("c"), R("d")] };
        var query = new QueryGroup { Children = [R("a"), inner] };
        var (_, builder) = Build(query);

        await builder.Find("[data-depth='1'] > .qb-group-head .mud-toggle-item:nth-of-type(2)").ClickAsync(new());
        inner.Logic.ShouldBe(QueryLogic.Or);
        Text(query).ShouldBe("(contains(tolower(Name),'a') and (contains(tolower(Name),'c') or contains(tolower(Name),'d')))");
        await builder.Find("[data-depth='0'] > .qb-group-head .mud-toggle-item:nth-of-type(2)").ClickAsync(new());
        query.Logic.ShouldBe(QueryLogic.Or);
    }

    [Fact]
    public async Task Selected_rules_wrap_into_a_new_group_and_a_group_unwraps()
    {
        var query = new QueryGroup { Children = [R("a"), R("b"), R("c")] };
        var (popovers, builder) = Build(query);

        foreach (var i in new[] { 0, 2 })
        {
            await builder.FindAll("[data-testid='query-rule'] [data-testid='query-select'] input, [data-testid='query-rule'] input[data-testid='query-select']")[i].ChangeAsync(new ChangeEventArgs { Value = true });
        }

        await builder.Find("[data-testid='query-wrap-selected']").ClickAsync(new());
        query.Children.Count.ShouldBe(2);
        var wrapper = query.Children.OfType<QueryGroup>().Single();
        wrapper.Logic.ShouldBe(QueryLogic.Or);
        wrapper.Children.Cast<QueryRule>().Select(r => r.Value).ShouldBe(["a", "c"]);
        query.Children[0].ShouldBe(wrapper);

        // (a or c) and b: unwrapping would turn it into a and c and b, so it is refused
        const string head = "[data-testid='query-group'][data-depth='1'] > .qb-group-head";
        var before = Text(query);
        await builder.FindAll(head)[0].QuerySelector("[data-testid='query-node-menu'] button, button[data-testid='query-node-menu']")!.ClickAsync(new());
        popovers.WaitForElement("[data-testid='query-unwrap']").ClassList.ShouldContain("mud-disabled");
        await builder.FindAll(head)[0].QuerySelector("[data-testid='query-node-menu'] button, button[data-testid='query-node-menu']")!.ClickAsync(new());
        QueryTree.Unwrap(query, wrapper).ShouldBeFalse();
        query.Children[0].ShouldBe(wrapper);
        Text(query).ShouldBe(before);

        // joined like its parent it keeps its meaning and unwraps
        await builder.Find(head + " .mud-toggle-item:nth-of-type(1)").ClickAsync(new());
        wrapper.Logic.ShouldBe(QueryLogic.And);
        var same = Text(query);
        await Menu(popovers, builder, head, 0, "query-unwrap");
        query.Children.Cast<QueryRule>().Select(r => r.Value).ShouldBe(["a", "c", "b"]);
        same.ShouldBe("((contains(tolower(Name),'a') and contains(tolower(Name),'c')) and contains(tolower(Name),'b'))");
        Text(query).ShouldBe("(contains(tolower(Name),'a') and contains(tolower(Name),'c') and contains(tolower(Name),'b'))");

        // a single node wraps from its menu
        await Menu(popovers, builder, "[data-testid='query-rule']", 1, "query-wrap");
        query.Children.OfType<QueryGroup>().Single().Children.Cast<QueryRule>().Single().Value.ShouldBe("c");
    }

    [Fact]
    public void MaxDepth_is_respected_when_moving()
    {
        var deep = new QueryGroup { Children = [new QueryGroup { Children = [R("x")] }] };
        var shallow = new QueryGroup { Children = [R("y")] };
        var query = new QueryGroup { Children = [deep, shallow, R("a")] };

        // deep needs 2 levels; inside the root's child (depth 1) that is 3
        QueryTree.CanMove(query, deep, shallow, 2).ShouldBeFalse();
        QueryTree.CanMove(query, deep, shallow, 3).ShouldBeTrue();
        QueryTree.Move(query, deep, shallow, 2).ShouldBeFalse();
        query.Children.ShouldContain(deep);
        QueryTree.CanMove(query, deep, deep, 5).ShouldBeFalse();
        QueryTree.CanMove(query, deep, (QueryGroup)deep.Children[0], 5).ShouldBeFalse();
        QueryTree.Targets(query, deep, 2).ShouldBeEmpty();

        // wrapping pushes the nodes one level down
        QueryTree.Wrap(query, [deep], 2).ShouldBeNull();
        QueryTree.Wrap(query, [query.Children[2]], 1).ShouldNotBeNull();

        // with MaxDepth 0 the builder offers no groups at all
        var (popovers, builder) = Build(new QueryGroup { Children = [R("a")] }, maxDepth: 0);
        builder.FindAll("[data-testid='query-add-group']").ShouldBeEmpty();
        builder.Find("[data-testid='query-rule'] [data-testid='query-node-menu'] button, [data-testid='query-rule'] button[data-testid='query-node-menu']").Click();
        popovers.WaitForElement("[data-testid='query-wrap']").ClassList.ShouldContain("mud-disabled");
    }

    [Fact]
    public void The_advanced_filter_of_a_table_uses_the_builder()
    {
        Services.AddSingleton<IODataClient>(new FakeODataClient().With("Gadgets", new DataTableTests.Gadget(Guid.NewGuid(), "Drill", "Tools")));
        AddAuthorization().SetAuthorized("Ada");
        Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<DataTableTests.Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Columns, (RenderFragment)(b =>
        {
            b.OpenComponent<PropertyColumn<DataTableTests.Gadget, string>>(0);
            b.AddComponentParameter(1, "Property", (System.Linq.Expressions.Expression<Func<DataTableTests.Gadget, string>>)(g => g.Name));
            b.CloseComponent();
        })));

        table.Find("[data-testid='advanced-filter']").Click();
        table.FindComponent<QueryBuilder>().ShouldNotBeNull();
        table.Find("[data-testid='advanced-panel'] [data-testid='query-add-group']").ShouldNotBeNull();
    }
}

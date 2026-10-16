using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Workflows;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Workflows;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class WorkflowColumnTests : ClientTestBase
{
    private static readonly Ticket First = new(Guid.CreateVersion7(), "First");
    private static readonly Ticket Second = new(Guid.CreateVersion7(), "Second");

    private static readonly FacetGroupDto States = new("WorkflowState", "Workflow state", "WorkflowState", FacetKind.CheckboxList, FacetOperator.Or,
        [Option("Open", 1), Option("Review", 1), new(JsonSerializer.SerializeToElement("me"), "Awaiting my approval", true, 1, "WorkflowAwaitsMe eq true")], true, int.MaxValue)
    {
        ValuePath = "WorkflowState",
        ValueKind = FacetValueKind.Enum,
        Combine = FacetCombine.Any,
    };

    private readonly FakeODataClient _odata = new();
    private readonly List<ODataQuery> _queries = [];
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public WorkflowColumnTests()
    {
        _odata.With("Tickets", First, Second).WithFacets(States);
        _odata.Queried += (_, query) => _queries.Add(query);
        var api = Substitute.For<IWorkflowApi>();
        api.GetStatesAsync("Tickets", Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(
            [new WorkflowStatusDto("Tickets", First.Id, "Review", [], null, []), new WorkflowStatusDto("Tickets", Second.Id, "Open", [], null, [])]);
        Services.AddSingleton<IODataClient>(_odata);
        Services.AddSingleton(api);
        Services.AddScoped<WorkflowStates>();
        Services.AddScoped<WorkflowCommands>();
        AddAuthorization().SetAuthorized("Ada");
        _popovers = Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    [Fact]
    public async Task The_column_filters_with_the_states_of_its_facet_and_sorts_on_the_server()
    {
        var table = RenderTable();
        table.WaitForAssertion(() => table.FindAll("[data-testid='workflow-state']").Select(s => s.TextContent.Trim()).ShouldBe(["Review", "Open"]));

        await Header(table).QuerySelector(".filter-button")!.ClickAsync(new());
        _popovers.WaitForElement("[data-testid='column-filter']");
        _popovers.Find("[data-testid='column-filter']").TextContent.ShouldContain("Awaiting my approval");
        await _popovers.Find("[data-option=\"WorkflowState eq 'Review'\"] input").ChangeAsync(new() { Value = true });

        table.WaitForAssertion(() => _queries.Last().Filter.ShouldBe("WorkflowState eq 'Review'"));
        table.WaitForAssertion(() => Header(table).QuerySelector(".filter-button.filtered").ShouldNotBeNull());

        await Header(table).QuerySelector(".sort-direction-icon, .mud-table-sort-label")!.ClickAsync(new());
        table.WaitForAssertion(() => _queries.Last().OrderBy.ShouldBe("WorkflowState asc"));
    }

    [Fact]
    public async Task Grouping_by_the_state_sorts_the_server_query_and_groups_by_the_loaded_states()
    {
        var table = RenderTable();

        await DataTableStateTests.OpenAsync(table, _popovers, "grouping", "[data-group='WorkflowState']");
        await _popovers.Find("[data-group='WorkflowState']").ClickAsync(new());

        table.WaitForAssertion(() => _queries.Last().OrderBy.ShouldBe("WorkflowState asc"));
        table.WaitForAssertion(() => table.FindAll("[data-group-key]").Select(g => g.TextContent).ShouldBe(["Review", "Open"]));
    }

    private static FacetOptionDto Option(string state, int count) =>
        new(JsonSerializer.SerializeToElement(state), state, true, count, $"WorkflowState eq '{state}'");

    private IRenderedComponent<TicketTable> RenderTable()
    {
        var table = Render<TicketTable>();
        table.WaitForAssertion(() => table.Markup.ShouldContain("First"));
        table.WaitForAssertion(() => table.FindAll("[data-testid='columns']").ShouldNotBeEmpty());
        return table;
    }

    private static AngleSharp.Dom.IElement Header(IRenderedComponent<TicketTable> table) =>
        table.FindAll("th.mud-table-cell").First(h => h.TextContent.Trim().StartsWith("State", StringComparison.Ordinal));

    public sealed record Ticket(Guid Id, string Title);
}

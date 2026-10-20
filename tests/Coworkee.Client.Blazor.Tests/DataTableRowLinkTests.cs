using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class DataTableRowLinkTests : ClientTestBase
{
    private static readonly Row[] Data = [new(Guid.CreateVersion7(), "Ada"), new(Guid.CreateVersion7(), "Bob")];

    public DataTableRowLinkTests()
    {
        Services.AddSingleton<IODataClient>(new Rows());
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public async Task A_row_with_an_href_navigates_on_click_and_has_an_open_link()
    {
        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.Columns, Columns()).Add(t => t.RowHref, r => $"/rows/{r.Id}"));
        table.WaitForAssertion(() => table.FindAll("tbody tr.cw-clickable").Count.ShouldBe(2));

        table.Find("[data-testid='open-row']").GetAttribute("href").ShouldBe($"/rows/{Data[0].Id}");
        await table.InvokeAsync(() => table.FindAll("tbody tr td")[0].Click());

        table.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith($"/rows/{Data[0].Id}"));
    }

    [Fact]
    public async Task Row_click_ignores_the_action_buttons_and_the_checkbox()
    {
        var clicked = new List<Row>();
        var edited = new List<Row>();
        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.Columns, Columns()).Add(t => t.MultiSelection, true)
            .Add(t => t.OnRowClick, r => clicked.Add(r)).Add(t => t.OnEdit, r => edited.Add(r)));
        table.WaitForAssertion(() => table.FindAll("tbody tr.cw-clickable").Count.ShouldBe(2));

        await table.InvokeAsync(() => table.FindAll("[data-testid='edit-row']")[1].Click());
        await table.InvokeAsync(() => table.FindAll("tbody tr input[type='checkbox']")[0].Change(true));
        table.WaitForAssertion(() => edited.ShouldBe([Data[1]]));
        clicked.ShouldBeEmpty();

        await table.InvokeAsync(() => table.FindAll("tbody tr")[1].QuerySelectorAll("td")[1].Click());
        await table.InvokeAsync(() => table.FindAll("[data-testid='open-row']")[0].Click());

        table.WaitForAssertion(() => clicked.ShouldBe([Data[1], Data[0]]));
    }

    [Fact]
    public void Rows_without_a_link_or_handler_are_not_clickable()
    {
        var table = Render<CoworkeeDataTable<Row>>(p => p.Add(t => t.EntitySet, "Rows").Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.FindAll("tbody tr").Count.ShouldBe(2));

        table.FindAll("tr.cw-clickable").ShouldBeEmpty();
        table.FindAll("[data-testid='open-row']").ShouldBeEmpty();
    }

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Row, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Row, string>.Property), (System.Linq.Expressions.Expression<Func<Row, string>>)(r => r.Name));
        builder.CloseComponent();
    };

    public sealed record Row(Guid Id, string Name);

    private sealed class Rows : IODataClient
    {
        public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ODataPage<T>((IReadOnlyList<T>)(object)Data.ToList(), Data.Length, []));
    }
}

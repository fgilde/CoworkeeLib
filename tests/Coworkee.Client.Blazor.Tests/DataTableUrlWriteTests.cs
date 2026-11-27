using Bunit;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Like WebAssembly, a navigation settles later: until then the address is still the old one.</summary>
public sealed class DataTableUrlWriteTests : ClientTestBase
{
    private readonly DeferredNavigation _nav = new();

    public DataTableUrlWriteTests()
    {
        var odata = new FakeODataClient();
        odata.With("Gadgets", new DataTableStateTests.Gadget(Guid.CreateVersion7(), "Drill", "Tools"));
        Services.AddSingleton<IODataClient>(odata);
        Services.AddSingleton<NavigationManager>(_nav);
        AddAuthorization().SetAuthorized("Ada");
    }

    [Fact]
    public void A_filter_change_from_the_page_does_not_navigate_back_to_the_old_address()
    {
        _nav.Settle("/gadgets?quick=inTransit");
        var table = RenderTable("Name eq 'a'");
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        _nav.Requested.Clear();

        // the page deselects its chip, its navigation is still on the way when the filter reaches the table
        _nav.NavigateTo("/gadgets", replace: true);
        table.Render(p => p.Add(t => t.Filter, "Name eq 'b'"));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));
        _nav.Flush();

        _nav.Requested.ShouldBe(["/gadgets"]);
        _nav.Uri.ShouldEndWith("/gadgets");
    }

    [Fact]
    public void The_table_still_writes_its_own_state_into_the_address()
    {
        _nav.Settle("/gadgets");
        var table = RenderTable(null);
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"));

        table.Find("input[data-testid='table-search'], [data-testid='table-search'] input").Input("dri");
        table.WaitForAssertion(() => _nav.Requested.ShouldNotBeEmpty(), TimeSpan.FromSeconds(3));
        _nav.Flush();

        DataTableState.Decode(System.Web.HttpUtility.ParseQueryString(new Uri(_nav.Uri).Query)["gadgets"])!.Search.ShouldBe("dri");
    }

    private IRenderedComponent<CoworkeeDataTable<DataTableStateTests.Gadget>> RenderTable(string? filter) =>
        Render<CoworkeeDataTable<DataTableStateTests.Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.SearchFields, ["Name"]).Add(t => t.Filter, filter).Add(t => t.Columns, DataTableStateTests.Columns()));

    private sealed class DeferredNavigation : NavigationManager
    {
        private readonly Queue<string> _queue = new();
        public List<string> Requested { get; } = [];

        public DeferredNavigation() => Initialize("http://localhost/", "http://localhost/");

        public void Settle(string path) => Apply(path);

        public void Flush()
        {
            while (_queue.Count > 0)
            {
                Apply(_queue.Dequeue());
            }
        }

        private void Apply(string uri)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            NotifyLocationChanged(false);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Requested.Add(ToAbsoluteUri(uri).PathAndQuery);
            _queue.Enqueue(uri);
        }
    }
}

using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Extensions;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

/// <summary>The header menu of a table over a local copy: the sort entries and the facet values with the counts of the browser's rows.</summary>
public sealed class LocalTableHeaderTests : BunitContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
    private readonly FakeServer _server = new();
    private readonly ClientEntities _entities;

    public LocalTableHeaderTests()
    {
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Drill", "Tools", 99));
        _server.Rows.Add(new Gadget(Guid.CreateVersion7(), "Lamp", "Light", 35));
        // the server's counts: the copy keeps its options and counts its own rows
        _server.Facets =
        [
            new FacetGroupDto("category", "Category", "Category", FacetKind.CheckboxList, FacetOperator.Or,
            [
                new FacetOptionDto(JsonSerializer.SerializeToElement("Tools"), "Tools", true, 40, "Category eq 'Tools'"),
                new FacetOptionDto(JsonSerializer.SerializeToElement("Light"), "Light", true, 30, "Category eq 'Light'"),
            ], true, 0),
        ];
        var api = Substitute.For<ICoworkeeApi>();
        api.GetSettingsAsync(default, default).ReturnsForAnyArgs([]);
        _entities = new ClientEntities(new ClientEntityApi(new HttpClient(_server) { BaseAddress = new Uri("http://app/") }), _server,
            new NoClientEntityStorage(), null, new ClientEntitySignal(), NullLogger<ClientEntities>.Instance, new ClientEntityModes(api, NullLogger<ClientEntityModes>.Instance));
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        var texts = Substitute.For<ILocalizationApi>();
        texts.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(texts);
        Services.AddSingleton<CoworkeeLocalizer>();
        Services.AddSingleton(_entities);
        Services.AddSingleton<IODataClient>(new ClientEntityODataClient(_server, _entities));
        Services.AddSingleton(Substitute.For<ITableViewStore>());
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddScoped<FileDownloader>();
        Services.AddSingleton(new Realtime.RealtimeClient(Substitute.For<Realtime.IRealtimeConnection>()));
        Services.AddSingleton(api);
        Services.AddScoped<Security.PermissionStore>();
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_header_menu_sorts_and_lists_the_facet_values_with_the_counts_of_the_local_rows()
    {
        var set = (await _entities.SetAsync<Gadget>("Gadgets", cancellationToken: Ct))!;
        await set.WhenLocalAsync(Ct);
        var popovers = Render<MudPopoverProvider>();
        var table = Render<CoworkeeDataTable<Gadget>>(p => p.Add(t => t.EntitySet, "Gadgets").Add(t => t.Toolbar, false).Add(t => t.ColumnFilterSort, true)
            .Add(t => t.RealtimeTopic, string.Empty).Add(t => t.Columns, Columns()));
        table.WaitForAssertion(() => table.Markup.ShouldContain("Drill"), Wait);
        table.WaitForAssertion(() => table.Instance.FacetGroups.ShouldNotBeEmpty(), Wait);
        var before = _server.Queries.Count;

        await table.InvokeAsync(() => table.FindAll("th.mud-table-cell").First(h => h.TextContent.Trim().StartsWith("Category", StringComparison.Ordinal))
            .QuerySelector(".filter-button")!.ClickAsync(new()));
        var menu = popovers.WaitForElement("[data-testid='column-filter']", Wait);

        menu.QuerySelector("[data-testid='column-sort']").ShouldNotBeNull();
        menu.QuerySelectorAll("[data-option]").Select(o => string.Concat(o.TextContent.Where(c => !char.IsWhiteSpace(c)))).ShouldBe(["Tools1", "Light1"]);
        await popovers.Find("[data-sort='asc'] input").ChangeAsync(new ChangeEventArgs { Value = true });
        table.WaitForAssertion(() => table.FindAll("tbody tr td:first-child").Select(c => c.TextContent.Trim()).ShouldBe(["Lamp", "Drill"]), Wait);
        await popovers.Find("[data-sort='desc'] input").ChangeAsync(new ChangeEventArgs { Value = true });

        table.WaitForAssertion(() => table.FindAll("tbody tr td:first-child").Select(c => c.TextContent.Trim()).ShouldBe(["Drill", "Lamp"]), Wait);
        table.Instance.CurrentState.Sort.ShouldBe([new DataTableSort("Category", true)]);
        _server.Queries.Count.ShouldBe(before, "the browser answered rows and counts");
    }

    private static RenderFragment Columns() => builder =>
    {
        builder.OpenComponent<PropertyColumn<Gadget, string>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Name));
        builder.CloseComponent();
        builder.OpenComponent<PropertyColumn<Gadget, string>>(2);
        builder.AddComponentParameter(3, nameof(PropertyColumn<Gadget, string>.Property), (System.Linq.Expressions.Expression<Func<Gadget, string>>)(g => g.Category));
        builder.CloseComponent();
    };
}

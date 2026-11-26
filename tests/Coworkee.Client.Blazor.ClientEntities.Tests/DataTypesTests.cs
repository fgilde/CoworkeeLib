using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.ClientEntities.Pages;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Query;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

// the Data types page (the former Local copies page): per set the copy settings and, behind the row, the System and Tenant filters and the link to another set
public sealed class DataTypesTests : BunitContext
{
    private readonly FakeServer _server = new();
    private readonly ICoworkeeApi _api = Substitute.For<ICoworkeeApi>();

    public DataTypesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        var texts = Substitute.For<ILocalizationApi>();
        texts.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(texts);
        Services.AddSingleton<CoworkeeLocalizer>();
        Services.AddSingleton(_server.Api);
        Services.AddSingleton(_api);
        var realtime = new Realtime.RealtimeClient(Substitute.For<Realtime.IRealtimeConnection>());
        Services.AddSingleton(realtime);
        Services.AddSingleton(new PermissionStore(_api, realtime));
        Services.AddSingleton<IClientEntityEngine>(new Engine());
        Services.AddSingleton(new CoworkeeClientOptions());
        _server.AdminSets.Add(new ClientEntityAdminSetDto("Gadgets", "Gadget", ClientEntityCopy.Memory, 50_000, 1000, 1, TimeSpan.FromMinutes(5), ClientEntityDefaultMode.Client,
            new Dictionary<string, ClientEntitySettingSource> { ["Copy"] = ClientEntitySettingSource.Override }, new ClientEntitySetOverride { Copy = ClientEntityCopy.Memory }));
        _api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([SettingsPermissions.Manage, DataFilterPermissions.ManageSystem, DataFilterPermissions.ManageTenant]);
        _api.GetDataFilterSetsAsync(default).ReturnsForAnyArgs(["Gadgets", "Widgets"]);
        _api.GetDataFilterFieldsAsync(default!, default).ReturnsForAnyArgs(
        [
            new DataFilterFieldDto("Name", QueryFieldType.Text, false, false, null, false),
            new DataFilterFieldDto("WidgetId", QueryFieldType.Guid, false, false, null, false),
        ]);
        var system = new DataFilterDto(Guid.NewGuid(), "Gadgets", DataFilterLayer.System, null,
            QueryJson.Serialize(new QueryGroup { Children = [new QueryRule { Field = "Name", Operator = QueryOperator.Equal, Value = "x" }] }), true, true, null);
        _api.GetDataFiltersAsync("Gadgets", DataFilterLayer.System, null, default).ReturnsForAnyArgs([system]);
        _api.GetDataFiltersAsync("Gadgets", DataFilterLayer.Tenant, null, default).Returns([]);
        _api.GetDataFilterLinksAsync(default).ReturnsForAnyArgs([new DataFilterLinkDto(Guid.NewGuid(), "Gadgets", "WidgetId", "Widgets")]);
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task The_data_types_page_shows_copy_settings_and_system_and_tenant_filters()
    {
        var page = Render<LocalCopies>();
        page.WaitForElement("[data-testid='copy-Gadgets']").GetAttribute("data-copy").ShouldBe("Memory");
        page.Markup.ShouldContain("Data types");

        await page.FindAll("tbody tr")[0].QuerySelector("button")!.ClickAsync(new());

        var filters = page.WaitForElement("[data-testid='filters-Gadgets']");
        page.WaitForAssertion(() => filters.QuerySelectorAll("[data-testid='data-filter-editor']").Select(e => e.GetAttribute("data-layer")).ShouldBe(["System", "Tenant"]));
        filters.QuerySelector("[data-testid='data-filter-link'] [data-testid='link-text']")!.TextContent.ShouldBe("Same data as Widgets via WidgetId");
    }

    [Fact]
    public async Task A_set_without_a_local_copy_still_has_its_system_and_tenant_filters()
    {
        var page = Render<LocalCopies>();
        page.WaitForElement("[data-testid='no-copy-Widgets']").TextContent.ShouldContain("Not available for local copies");

        await page.FindAll("tbody tr")[1].QuerySelector("button")!.ClickAsync(new());

        var filters = page.WaitForElement("[data-testid='filters-Widgets']");
        page.WaitForAssertion(() => filters.QuerySelectorAll("[data-testid='data-filter-editor']").Select(e => e.GetAttribute("data-set") + ":" + e.GetAttribute("data-layer")).ShouldBe(["Widgets:System", "Widgets:Tenant"]));
        page.FindAll("[data-testid='edit-Widgets']").ShouldBeEmpty();
    }

    [Fact]
    public async Task With_only_the_tenant_permission_the_page_shows_only_the_tenant_editors()
    {
        _api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([DataFilterPermissions.ManageTenant]);
        var page = Render<LocalCopies>();
        page.WaitForAssertion(() => page.FindAll("tbody tr").Count.ShouldBe(2));

        // no copy settings without Settings.Manage
        page.FindAll("[data-testid^='copy-'], [data-testid^='no-copy-'], [data-testid^='edit-']").ShouldBeEmpty();
        await page.FindAll("tbody tr")[0].QuerySelector("button")!.ClickAsync(new());

        var filters = page.WaitForElement("[data-testid='filters-Gadgets']");
        page.WaitForAssertion(() => filters.QuerySelectorAll("[data-testid='data-filter-editor']").Select(e => e.GetAttribute("data-layer")).ShouldBe(["Tenant"]));
        filters.QuerySelectorAll("[data-testid='data-filter-link']").ShouldBeEmpty();
    }

    [Fact]
    public void With_only_Settings_Manage_the_copy_settings_show_without_filters()
    {
        _api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([SettingsPermissions.Manage]);
        var page = Render<LocalCopies>();

        page.WaitForElement("[data-testid='copy-Gadgets']");
        page.FindAll("tbody tr").Count.ShouldBe(1);
        page.FindAll("tbody tr")[0].QuerySelectorAll("button[aria-label='Edit']").Count.ShouldBe(1);
        _api.DidNotReceiveWithAnyArgs().GetDataFilterSetsAsync(default);
    }

    private sealed class Engine : IClientEntityEngine
    {
        public bool IsAvailable => true;

        public IClientEntityStore<T>? Open<T>(string key) => null;
    }
}

using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.ClientEntities.Pages;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.DataFilters;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using NSubstitute;

namespace Coworkee.Client.Blazor.ClientEntities.Tests;

public sealed class LocalCopiesTests : BunitContext
{
    private static readonly Dictionary<string, ClientEntitySettingSource> Defaults = new()
    {
        ["Copy"] = ClientEntitySettingSource.Default, ["MaxRows"] = ClientEntitySettingSource.Default, ["BatchSize"] = ClientEntitySettingSource.Default,
        ["ParallelPages"] = ClientEntitySettingSource.Default, ["RefreshInterval"] = ClientEntitySettingSource.Default, ["DefaultMode"] = ClientEntitySettingSource.Default,
    };

    private readonly FakeServer _server = new();
    private readonly ICoworkeeApi _api = Substitute.For<ICoworkeeApi>();
    private readonly IRenderedComponent<MudDialogProvider> _dialogs;
    private readonly Engine _engine = new();
    private readonly Connection _connection = new();

    public LocalCopiesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServicesWithExtensions();
        var texts = Substitute.For<ILocalizationApi>();
        texts.GetTextsAsync(default!, default).ReturnsForAnyArgs(call => new Contracts.Localization.TextsDto(call.Arg<string>(), new Dictionary<string, string>()));
        Services.AddSingleton(texts);
        Services.AddSingleton<CoworkeeLocalizer>();
        Services.AddSingleton(_server.Api);
        Services.AddSingleton(_api);
        Services.AddSingleton<IClientEntityEngine>(_engine);
        _api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([SettingsPermissions.Manage]);
        var realtime = new RealtimeClient(_connection);
        Services.AddSingleton(realtime);
        Services.AddSingleton(new Security.PermissionStore(_api, realtime));
        Services.AddSingleton(new CoworkeeClientOptions());
        Services.AddScoped<Dialogs.CoworkeeDialogOptions>();
        Services.AddSingleton<Dialogs.DialogOptionsContributor>();
        _server.AdminSets.Add(new ClientEntityAdminSetDto("Gadgets", "Gadget", null, 50_000, 1000, 1, TimeSpan.FromMinutes(5), ClientEntityDefaultMode.Client,
            new Dictionary<string, ClientEntitySettingSource>(Defaults) { ["Copy"] = ClientEntitySettingSource.Attribute }, null));
        _server.AdminSets.Add(new ClientEntityAdminSetDto("Orders", "Order", ClientEntityCopy.Sqlite, 123, 1000, 1, TimeSpan.FromSeconds(30), ClientEntityDefaultMode.Server,
            new Dictionary<string, ClientEntitySettingSource>(Defaults)
            {
                ["Copy"] = ClientEntitySettingSource.Override, ["MaxRows"] = ClientEntitySettingSource.Override, ["RefreshInterval"] = ClientEntitySettingSource.Code,
                ["DefaultMode"] = ClientEntitySettingSource.Override,
            },
            new ClientEntitySetOverride { Copy = ClientEntityCopy.Sqlite, MaxRows = 123, DefaultMode = ClientEntityDefaultMode.Server }));
        _api.GetAppConfigurationAsync(ClientEntitySetOverride.Section, default).ReturnsForAnyArgs(new AppConfigurationValuesDto(ClientEntitySetOverride.Section,
            JsonSerializer.SerializeToElement(new { Orders = new { Copy = "Sqlite", MaxRows = 123, DefaultMode = "Server" }, Tools = new { Copy = "Off" } }), default, []));
        _api.SaveAppConfigurationAsync(default!, default, default).ReturnsForAnyArgs(call => new AppConfigurationValuesDto(ClientEntitySetOverride.Section, call.ArgAt<JsonElement>(1), default, []));
        _api.GetDataFilterSetsAsync(default).ReturnsForAnyArgs(["Gadgets", "Orders", "Tools"]);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    [Fact]
    public void Lists_every_set_with_its_copy_values_and_what_decided_them()
    {
        var page = Render<LocalCopies>();

        page.WaitForAssertion(() => page.FindAll("[data-testid^='copy-']").Count.ShouldBe(2));
        var gadgets = page.Find("[data-testid='copy-Gadgets']");
        gadgets.GetAttribute("data-copy").ShouldBe("Auto");
        gadgets.TextContent.ShouldContain("Automatic");
        gadgets.QuerySelector("[data-testid='source']")!.GetAttribute("data-source").ShouldBe("Attribute");
        var orders = page.Find("[data-testid='copy-Orders']");
        orders.GetAttribute("data-copy").ShouldBe("Sqlite");
        orders.QuerySelector("[data-testid='source']")!.GetAttribute("data-source").ShouldBe("Override");
        var row = orders.Closest("tr")!;
        row.TextContent.ShouldContain("123");
        row.TextContent.ShouldContain("30 s");
        row.TextContent.ShouldContain("Server");
        row.QuerySelectorAll("[data-testid='source']").Select(s => s.GetAttribute("data-source")).ShouldBe(["Override", "Override", "Code", "Override"]);
        // only a set with an override can go back to the code
        page.Find("[data-testid='reset-Gadgets']").HasAttribute("disabled").ShouldBeTrue();
        page.Find("[data-testid='reset-Orders']").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void Without_working_sqlite_in_the_app_sets_set_to_sqlite_are_flagged()
    {
        var page = Render<LocalCopies>();

        page.WaitForElement("[data-testid='sqlite-missing']").TextContent.ShouldContain("SQLite is not available in this app");
        page.Find("[data-testid='copy-Orders'] [data-testid='sqlite-fallback']");
        page.Find("[data-testid='copy-Gadgets']").QuerySelector("[data-testid='sqlite-fallback']").ShouldBeNull();
    }

    [Fact]
    public void With_an_available_engine_nothing_is_flagged()
    {
        _engine.Available = true;

        var page = Render<LocalCopies>();

        page.WaitForAssertion(() => page.FindAll("[data-testid^='copy-']").Count.ShouldBe(2));
        page.FindAll("[data-testid='sqlite-missing']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Resetting_saves_the_section_without_the_set_and_keeps_the_others()
    {
        var page = Render<LocalCopies>();

        await page.WaitForElement("[data-testid='reset-Orders']").ClickAsync(new());

        await _api.Received(1).SaveAppConfigurationAsync(ClientEntitySetOverride.Section,
            Arg.Is<JsonElement>(v => v.EnumerateObject().All(p => p.Name != "Orders") && v.GetProperty("Tools").GetProperty("copy").GetString() == "Off"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_dialog_edits_the_override_and_saves_only_what_is_set()
    {
        var page = Render<LocalCopies>();
        var editing = page.WaitForElement("[data-testid='edit-Gadgets']", TimeSpan.FromSeconds(15)).ClickAsync(new());
        // the object edit form renders its fields after its own setup: change and submit only once they are there (CI saw a half set-up form)
        _dialogs.WaitForAssertion(() =>
        {
            var labels = _dialogs.FindAll("label").Select(l => l.TextContent.Trim()).ToList();
            labels.ShouldContain("Row limit");
            labels.ShouldContain("Refresh (seconds)");
        }, TimeSpan.FromSeconds(15));

        var rows = _dialogs.FindAll("input").First(i => i.Id == _dialogs.FindAll("label").First(l => l.TextContent.Trim() == "Row limit").GetAttribute("for"));
        await rows.ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "777" });
        await _dialogs.Find("form").SubmitAsync();
        await editing;

        await _api.Received(1).SaveAppConfigurationAsync(ClientEntitySetOverride.Section,
            Arg.Is<JsonElement>(v => v.GetProperty("Gadgets").GetProperty("maxRows").GetInt32() == 777 && v.GetProperty("Gadgets").GetProperty("copy").ValueKind == JsonValueKind.Null
                && v.GetProperty("Orders").GetProperty("maxRows").GetInt32() == 123),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_form_turns_empty_fields_into_no_override()
    {
        var form = LocalCopies.LocalCopyForm.From(new ClientEntitySetOverride { Copy = ClientEntityCopy.Memory, RefreshInterval = TimeSpan.FromSeconds(90) });

        (form.Copy, form.RefreshSeconds, form.DefaultMode).ShouldBe(("Memory", 90, string.Empty));
        form.ToOverride().ShouldSatisfyAllConditions(o => o.Copy.ShouldBe(ClientEntityCopy.Memory), o => o.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(90)), o => o.DefaultMode.ShouldBeNull());
        new LocalCopies.LocalCopyForm { MaxRows = 0 }.ToOverride().IsEmpty.ShouldBeTrue();
    }

    private void Overview(DataFilterLayer layer, string? tenant, params DataFilterSetStateDto[] sets)
    {
        _api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([SettingsPermissions.Manage, layer == DataFilterLayer.System ? DataFilterPermissions.ManageSystem : DataFilterPermissions.ManageTenant]);
        _api.GetDataFilterOverviewAsync(default).ReturnsForAnyArgs(new DataFilterOverviewDto(layer, tenant, sets));
        _api.GetDataFiltersAsync(default, default, default, default).ReturnsForAnyArgs([]);
    }

    private static IEnumerable<string> Names(IRenderedComponent<LocalCopies> page) => page.FindAll("td [data-testid^='copy-'],td [data-testid^='no-copy-']").Select(e => e.GetAttribute("data-testid")!);

    [Fact]
    public void Search_narrows_the_sets_by_name_and_type()
    {
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();
        page.WaitForAssertion(() => Names(page).Count().ShouldBe(3));

        page.Find("[data-testid='sets-search']").Input("ORD");

        page.WaitForAssertion(() => Names(page).ShouldBe(["copy-Orders"]));
        // "Gadget" is the type of Gadgets
        page.Find("[data-testid='sets-search']").Input("gadget");
        page.WaitForAssertion(() => Names(page).ShouldBe(["copy-Gadgets"]));
    }

    [Fact]
    public async Task Chips_show_local_copies_only_and_filtered_only()
    {
        Overview(DataFilterLayer.Tenant, "Acme", new DataFilterSetStateDto("Tools", 2, false));
        _server.AdminSets[0] = _server.AdminSets[0] with { Copy = ClientEntityCopy.Off };
        var page = Render<LocalCopies>();
        page.WaitForAssertion(() => Names(page).Count().ShouldBe(3));

        await page.Find("[data-testid='chip-copies']").ClickAsync(new());
        page.WaitForAssertion(() => Names(page).ShouldBe(["copy-Orders"]));
        await page.Find("[data-testid='chip-copies']").ClickAsync(new());
        await page.Find("[data-testid='chip-filtered']").ClickAsync(new());
        page.WaitForAssertion(() => Names(page).ShouldBe(["no-copy-Tools"]));
    }

    [Fact]
    public async Task The_filter_icon_is_coloured_with_a_badge_and_toggles_the_panel()
    {
        Overview(DataFilterLayer.System, null, new DataFilterSetStateDto("Orders", 1, true));
        var page = Render<LocalCopies>();
        page.WaitForElement("[data-testid='filter-Orders']");

        page.Find("[data-testid='filter-Orders']").ClassList.ShouldContain("mud-primary-text");
        page.Find("[data-testid='filter-Orders']").Closest("td")!.QuerySelector(".mud-badge")!.TextContent.Trim().ShouldBe("2");
        page.Find("[data-testid='filter-Gadgets']").ClassList.ShouldNotContain("mud-primary-text");
        page.Find("[data-testid='filter-Gadgets']").Closest("td")!.QuerySelector(".mud-badge")?.TextContent.Trim().ShouldBeNullOrEmpty();

        await page.Find("[data-testid='filter-Orders']").ClickAsync(new());
        page.WaitForElement("[data-testid='filters-Orders']");
        await page.Find("[data-testid='filter-Orders']").ClickAsync(new());
        page.WaitForAssertion(() => page.FindAll("[data-testid='filters-Orders']").ShouldBeEmpty());
    }

    [Fact]
    public async Task The_system_tenant_panel_shows_the_system_layer_and_the_link_editor()
    {
        Overview(DataFilterLayer.System, null);
        var page = Render<LocalCopies>();

        await page.WaitForElement("[data-testid='filter-Orders']").ClickAsync(new());

        var panel = page.WaitForElement("[data-testid='filters-Orders']");
        panel.TextContent.ShouldContain("Filter for all tenants");
        page.WaitForAssertion(() => panel.QuerySelector("[data-testid='data-filter-list']")!.GetAttribute("data-layer").ShouldBe("System"));
        page.WaitForAssertion(() => panel.QuerySelector("[data-testid='data-filter-link']").ShouldNotBeNull());
    }

    [Fact]
    public async Task A_tenant_panel_shows_its_tenant_layer_titled_with_its_name()
    {
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();

        await page.WaitForElement("[data-testid='filter-Orders']").ClickAsync(new());

        var panel = page.WaitForElement("[data-testid='filters-Orders']");
        panel.TextContent.ShouldContain("Filter for tenant Acme");
        page.WaitForAssertion(() => panel.QuerySelectorAll("[data-testid='data-filter-list']").Select(l => l.GetAttribute("data-layer")).ShouldBe(["Tenant"]));
        panel.QuerySelector("[data-testid='data-filter-link']").ShouldBeNull();
    }

    [Fact]
    public void Without_filter_rights_no_filter_icon()
    {
        var page = Render<LocalCopies>();

        page.WaitForElement("[data-testid='edit-Orders']");
        page.FindAll("[data-testid^='filter-']").ShouldBeEmpty();
        page.FindAll("[data-testid='chip-filtered']").ShouldBeEmpty();
    }

    [Fact]
    public void Without_copy_rights_only_the_filter_icon()
    {
        _api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([DataFilterPermissions.ManageTenant]);
        _api.GetDataFilterOverviewAsync(default).ReturnsForAnyArgs(new DataFilterOverviewDto(DataFilterLayer.Tenant, "Acme", []));

        var page = Render<LocalCopies>();

        page.WaitForElement("[data-testid='filter-Orders']");
        page.FindAll("[data-testid^='edit-'],[data-testid^='reset-']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Counts_reload_on_the_data_filter_signals()
    {
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();
        page.WaitForElement("[data-testid='filter-Orders']");
        await _api.Received(1).GetDataFilterOverviewAsync(Arg.Any<CancellationToken>());
        _api.GetDataFilterOverviewAsync(default).ReturnsForAnyArgs(new DataFilterOverviewDto(DataFilterLayer.Tenant, "Acme", [new DataFilterSetStateDto("Orders", 3, false)]));

        _connection.Raise(DataFilterTopics.TenantChanged);

        page.WaitForAssertion(() => page.Find("[data-testid='filter-Orders']").Closest("td")!.QuerySelector(".mud-badge")!.TextContent.Trim().ShouldBe("3"));
    }

    [Fact]
    public async Task An_open_panel_stays_open_when_the_overview_reloads()
    {
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();
        await page.WaitForElement("[data-testid='filter-Orders']").ClickAsync(new());
        page.WaitForElement("[data-testid='filters-Orders']");
        _api.GetDataFilterOverviewAsync(default).ReturnsForAnyArgs(new DataFilterOverviewDto(DataFilterLayer.Tenant, "Acme", [new DataFilterSetStateDto("Orders", 3, false)]));

        _connection.Raise(DataFilterTopics.TenantChanged);

        page.WaitForAssertion(() => page.Find("[data-testid='filter-Orders']").Closest("td")!.QuerySelector(".mud-badge")!.TextContent.Trim().ShouldBe("3"));
        page.FindAll("[data-testid='filters-Orders']").Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_open_panel_stays_open_while_the_search_still_matches_and_after_a_reload_of_the_sets()
    {
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();
        await page.WaitForElement("[data-testid='filter-Orders']").ClickAsync(new());
        page.WaitForElement("[data-testid='filters-Orders']");

        page.Find("[data-testid='sets-search']").Input("o");
        page.WaitForAssertion(() => Names(page).ShouldBe(["copy-Orders", "no-copy-Tools"]));
        page.Find("[data-testid='sets-search']").Input("ord");
        page.WaitForAssertion(() => Names(page).ShouldBe(["copy-Orders"]));
        page.FindAll("[data-testid='filters-Orders']").Count.ShouldBe(1);

        _connection.Raise(ClientEntityTopics.Changed);
        await Task.Delay(ClientEntities.SettingsDelay + TimeSpan.FromMilliseconds(300), Xunit.TestContext.Current.CancellationToken);
        page.FindAll("[data-testid='filters-Orders']").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Local_copies_only_keeps_sets_with_an_automatic_copy_and_drops_server_only_ones()
    {
        // Copy null: the code marks the set and the clients choose memory or SQLite themselves; Off: not mirrored
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();
        page.WaitForAssertion(() => Names(page).Count().ShouldBe(3));

        await page.Find("[data-testid='chip-copies']").ClickAsync(new());

        page.WaitForAssertion(() => Names(page).ShouldBe(["copy-Gadgets", "copy-Orders"]));
    }

    [Fact]
    public async Task Nothing_matching_says_so_instead_of_claiming_no_set_can_have_a_copy()
    {
        Overview(DataFilterLayer.Tenant, "Acme");
        var page = Render<LocalCopies>();
        page.WaitForAssertion(() => Names(page).Count().ShouldBe(3));

        page.Find("[data-testid='sets-search']").Input("zzz");

        page.WaitForAssertion(() => page.Find("[data-testid='local-copies']").TextContent.ShouldContain("No data type matches the search or filters."));
        page.Find("[data-testid='local-copies']").TextContent.ShouldNotContain("No entity set can have a local copy");
    }

    /// <summary>A hub connection the test can send signals through.</summary>
    private sealed class Connection : IRealtimeConnection
    {
        private Action<RealtimeEnvelope>? _handler;

#pragma warning disable CS0067
        public event Func<Task>? Reconnected;
#pragma warning restore CS0067

        public Task StartAsync() => Task.CompletedTask;

        public Task InvokeAsync(string method, string topic) => Task.CompletedTask;

        public void OnEvent(Action<RealtimeEnvelope> handler) => _handler = handler;

        public void Raise(string topic) => _handler!(new RealtimeEnvelope(topic, "Signal", default));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The SQLite engine of an app, working or not (a client built without native SQLite).</summary>
    private sealed class Engine : IClientEntityEngine
    {
        public bool Available { get; set; }

        public bool IsAvailable => Available;

        public IClientEntityStore<T>? Open<T>(string key) => null;
    }
}

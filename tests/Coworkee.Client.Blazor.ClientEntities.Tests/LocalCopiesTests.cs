using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.ClientEntities.Pages;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
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

    /// <summary>The SQLite engine of an app, working or not (a client built without native SQLite).</summary>
    private sealed class Engine : IClientEntityEngine
    {
        public bool Available { get; set; }

        public bool IsAvailable => Available;

        public IClientEntityStore<T>? Open<T>(string key) => null;
    }
}

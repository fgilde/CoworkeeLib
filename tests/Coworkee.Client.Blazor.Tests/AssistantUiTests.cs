using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

// switching the language sets the process-wide culture, so these run with the other language tests
[Collection(nameof(LocalizationTests))]
public sealed class AssistantUiTests : ClientTestBase
{
    private const string ProductsView = "Catalog.Products.View";
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;
    private static readonly ThemeDto Default = new(Guid.NewGuid(), "Default", true, true, Empty, Empty, null, null, null, null, 1);
    private static readonly ThemeDto Ocean = new(Guid.NewGuid(), "Ocean", true, false, Empty, Empty, null, null, null, null, 1);
    private readonly IAssistantApi _api = Substitute.For<IAssistantApi>();

    public AssistantUiTests()
    {
        Services.AddSingleton(_api);
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Products", "/catalog/products", Icons.Material.Outlined.Inventory, ProductsView, Group: "Catalog", Description: "Products, prices and stock."),
            new CoworkeeNavItem("Users", "/admin/users", Icons.Material.Outlined.Person, "Identity.Users.View", Group: "Administration")));
        Services.AddOptions<NavigationMenuOptions>();
        Api.GetThemesAsync(default).ReturnsForAnyArgs([Default, Ocean]);
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([ProductsView, AiPermissions.Chat]);
        Localization.GetLanguagesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns([new LanguageDto(null, "en", "English", true, true), new LanguageDto(null, "de", "Deutsch", true, false)]);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Guid.NewGuid().ToString()))
            .SetPolicies(Security.PermissionPolicy.For(ProductsView), Security.PermissionPolicy.For(AiPermissions.Chat));
        _api.GetSessionsAsync(default, default).ReturnsForAnyArgs([]);
    }

    private IAssistantClientAction Action(string name) => Services.GetServices<IAssistantClientAction>().Single(a => a.Name == name);

    private Task<AssistantActionResult> RunAsync(string name, string arguments) =>
        Action(name).RunAsync(JsonDocument.Parse(arguments).RootElement, Xunit.TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_ui_context_lists_only_what_the_user_sees()
    {
        await Services.GetRequiredService<Localization.CoworkeeLocalizer>().InitializeAsync("en");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/catalog/products/42");

        var ui = await Services.GetRequiredService<AssistantUiContext>().BuildAsync(Xunit.TestContext.Current.CancellationToken);

        ui.Pages!.ShouldContain(new AiUiPage("Products", "/catalog/products", "Catalog", "Products, prices and stock."));
        ui.Pages!.ShouldNotContain(p => p.Href == "/admin/users");
        ui.Current.ShouldBe(new AiUiPage("Products", "/catalog/products/42", "Catalog"));
        ui.Themes.ShouldBe(["Default", "Ocean"]);
        ui.Mode.ShouldBe("system");
        ui.Languages.ShouldBe(["en", "de"]);
        ui.Language.ShouldBe("en");
        ui.RightToLeft.ShouldBeFalse();
        ui.Actions!.Select(a => a.Name).ShouldBe(["navigate", "set_theme", "set_color_mode", "set_right_to_left", "set_language"], ignoreOrder: true);
        ui.Actions!.ShouldAllBe(a => a.Parameters!.Value.GetProperty("type").GetString() == "object");
    }

    [Fact]
    public async Task The_built_in_actions_change_the_ui()
    {
        await Services.GetRequiredService<Localization.CoworkeeLocalizer>().InitializeAsync("en");

        (await RunAsync("set_theme", """{"name":"ocean"}""")).ShouldBe(new AssistantActionResult(true, "Switched to the theme Ocean"));
        Services.GetRequiredService<ThemeService>().Current!.Name.ShouldBe("Ocean");
        (await RunAsync("set_color_mode", """{"mode":"dark"}""")).ShouldBe(new AssistantActionResult(true, "Switched to dark mode"));
        Services.GetRequiredService<ThemeService>().Mode.ShouldBe("dark");
        (await RunAsync("set_right_to_left", """{"enabled":true}""")).Succeeded.ShouldBeTrue();
        Services.GetRequiredService<LayoutPreferences>().RightToLeft.ShouldBeTrue();
        (await RunAsync("navigate", """{"href":"/catalog/products?x=1"}""")).Succeeded.ShouldBeTrue();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/catalog/products?x=1");
        (await RunAsync("set_language", """{"culture":"de"}""")).Succeeded.ShouldBeTrue();
        Services.GetRequiredService<Localization.CoworkeeLocalizer>().Culture.ShouldBe("de");
        await Api.Received().SetSettingsAsync(Contracts.Settings.SettingScope.User,
            Arg.Is<IReadOnlyDictionary<string, string?>>(d => d[ThemeSettings.Mode] == "dark"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_values_are_refused_with_a_note()
    {
        await Services.GetRequiredService<Localization.CoworkeeLocalizer>().InitializeAsync("en");

        (await RunAsync("set_theme", """{"name":"Neon"}""")).ShouldBe(new AssistantActionResult(false, "Unknown theme: Neon"));
        (await RunAsync("set_color_mode", """{"mode":"pink"}""")).ShouldBe(new AssistantActionResult(false, "Unknown color mode: pink"));
        (await RunAsync("set_language", """{"culture":"xx"}""")).ShouldBe(new AssistantActionResult(false, "Unknown language: xx"));
        (await RunAsync("set_right_to_left", """{"enabled":"maybe"}""")).Succeeded.ShouldBeFalse();
        (await RunAsync("navigate", """{"href":"https://evil.test/"}""")).Succeeded.ShouldBeFalse();
        (await RunAsync("navigate", """{"href":"//evil.test/"}""")).Succeeded.ShouldBeFalse();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldNotContain("evil");
        Services.GetRequiredService<ThemeService>().Mode.ShouldBe("system");
    }

    [Fact]
    public async Task The_window_sends_the_ui_runs_client_actions_and_notes_them()
    {
        var session = new AiSessionDto(Guid.NewGuid(), "Dark", DateTimeOffset.Now);
        var answer = new AiMessageDto(Guid.NewGuid(), "assistant", "Done.", [], [], null, DateTimeOffset.Now);
        _api.CreateSessionAsync(default).ReturnsForAnyArgs(session);
        _api.SendAsync(session.Id, "dark please", Arg.Any<AiUiContext?>(), Arg.Any<CancellationToken>()).Returns(Stream(
            new AiStreamEvent(AiStreamEvent.ClientAction, Action: new AiClientActionCall("c1", "set_color_mode", """{"mode":"dark"}""")),
            new AiStreamEvent(AiStreamEvent.ClientAction, Action: new AiClientActionCall("c2", "beam_me_up", "{}")),
            new AiStreamEvent(AiStreamEvent.Done, Message: answer)));
        _api.GetSessionAsync(session.Id, default).ReturnsForAnyArgs(new AiSessionDetailDto(session.Id, "Dark", DateTimeOffset.Now,
            [new AiMessageDto(Guid.NewGuid(), "user", "dark please", [], [], null, DateTimeOffset.Now), answer]));
        var window = Render<AssistantWindow>();

        window.Find("[data-testid='assistant-input']").Input("dark please");
        await window.Find("[data-testid='assistant-send']").ClickAsync(new());

        window.WaitForAssertion(() => window.FindAll("[data-testid='assistant-action']").Count.ShouldBe(2));
        var notes = window.FindAll("[data-testid='assistant-action']");
        notes[0].TextContent.ShouldContain("Switched to dark mode");
        notes[0].GetAttribute("data-succeeded").ShouldBe("true");
        notes[1].TextContent.ShouldContain("beam_me_up");
        notes[1].GetAttribute("data-succeeded").ShouldBe("false");
        Services.GetRequiredService<ThemeService>().Mode.ShouldBe("dark");
        _api.Received(1).SendAsync(session.Id, "dark please", Arg.Is<AiUiContext?>(u => u!.Actions!.Any(a => a.Name == "set_color_mode")), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<AiStreamEvent> Stream(params AiStreamEvent[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}

using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Theming;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>Where the assistant shows (setting <see cref="AiSettingNames.Display"/>): floating at either corner, in the app bar, in the navigation or nowhere but Ctrl+I.</summary>
public sealed class AssistantDisplayTests : ClientTestBase
{
    private readonly IAssistantApi _api = Substitute.For<IAssistantApi>();

    public AssistantDisplayTests()
    {
        Services.AddSingleton(_api);
        Services.AddSingleton<INavigationContributor, AdminNavigation>();
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Api.GetMyPermissionsAsync(default).ReturnsForAnyArgs([AiPermissions.Chat]);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Guid.NewGuid().ToString())).SetPolicies(Security.PermissionPolicy.For(AiPermissions.Chat));
        _api.GetStatusAsync(default).ReturnsForAnyArgs(new AiStatusDto(true));
        _api.GetSessionsAsync(default, default).ReturnsForAnyArgs([]);
    }

    private async Task DisplayAsync(string display)
    {
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { [AiSettingNames.Display] = display });
        await Services.GetRequiredService<ThemeService>().LoadAsync();
    }

    private IRenderedComponent<CoworkeeLayout> RenderLayout() =>
        Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

    [Fact]
    public async Task The_floating_button_sits_at_the_chosen_corner()
    {
        await DisplayAsync(AssistantDisplay.BottomLeft);

        var launcher = Render<AssistantLauncher>();

        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-fab']"));
        launcher.Find("[data-testid='assistant-launcher']").ClassList.ShouldContain("left");
        Render<AssistantLauncher>(p => p.Add(l => l.InAppBar, true)).FindAll("button").ShouldBeEmpty();
    }

    [Fact]
    public async Task In_the_app_bar_the_button_moves_there_and_the_shortcut_still_opens_the_window()
    {
        await DisplayAsync(AssistantDisplay.AppBar);
        var launcher = Render<AssistantLauncher>();
        var button = Render<AssistantLauncher>(p => p.Add(l => l.InAppBar, true));

        button.WaitForAssertion(() => button.Find("[data-testid='assistant-app-bar']"));
        launcher.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty();
        await button.Find("[data-testid='assistant-app-bar']").ClickAsync(new());
        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-window']"));

        await launcher.InvokeAsync(() => launcher.Instance.ToggleFromHotkey());
        launcher.WaitForAssertion(() => launcher.FindAll("[data-testid='assistant-window']").ShouldBeEmpty());
    }

    [Fact]
    public async Task In_the_app_bar_it_sits_before_the_about_button()
    {
        await DisplayAsync(AssistantDisplay.AppBar);

        var layout = RenderLayout();

        layout.WaitForAssertion(() => layout.Find(".mud-appbar [data-testid='assistant-app-bar']"));
        var bar = layout.Find(".mud-appbar").InnerHtml;
        bar.IndexOf("assistant-app-bar", StringComparison.Ordinal).ShouldBeLessThan(bar.IndexOf("about-button", StringComparison.Ordinal));
        layout.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Off_shows_no_button_but_the_shortcut_works()
    {
        await DisplayAsync(AssistantDisplay.Off);
        var launcher = Render<AssistantLauncher>();
        var button = Render<AssistantLauncher>(p => p.Add(l => l.InAppBar, true));

        await launcher.InvokeAsync(() => launcher.Instance.ToggleFromHotkey());

        launcher.WaitForAssertion(() => launcher.Find("[data-testid='assistant-window']"));
        launcher.FindAll("[data-testid='assistant-fab']").ShouldBeEmpty();
        button.FindAll("button").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AssistantDisplay.Navigation, true)]
    [InlineData(AssistantDisplay.BottomRight, false)]
    public async Task The_navigation_entry_is_there_only_when_chosen(string display, bool linked)
    {
        await DisplayAsync(display);

        var layout = RenderLayout();
        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.FindAll("[data-nav='/assistant']").Count.ShouldBe(linked ? 1 : 0));
    }
}

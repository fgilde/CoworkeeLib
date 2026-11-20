using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>CoworkeeAppShell: Coworkee's startup and providers around a layout of the app's own, without the admin chrome.</summary>
public sealed class AppShellComponentTests : ClientTestBase
{
    public AppShellComponentTests() => Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));

    private IRenderedComponent<CoworkeeAppShell> RenderShell(Action<ComponentParameterCollectionBuilder<CoworkeeAppShell>>? configure = null) =>
        Render<CoworkeeAppShell>(p =>
        {
            p.Add(s => s.ChildContent, shell => b => b.AddMarkupContent(0, $"<main id='own'>own layout, dark={shell.Dark}</main>"));
            configure?.Invoke(p);
        });

    [Fact]
    public void An_own_layout_renders_inside_the_providers_without_the_admin_chrome()
    {
        AddAuthorization().SetAuthorized("Ada");

        var shell = RenderShell();

        shell.WaitForAssertion(() => shell.Find("#own").TextContent.ShouldContain("own layout"));
        shell.FindComponents<MudThemeProvider>().ShouldHaveSingleItem();
        shell.FindComponents<MudDialogProvider>().ShouldHaveSingleItem();
        shell.FindComponents<MudPopoverProvider>().ShouldHaveSingleItem();
        shell.FindComponents<MudSnackbarProvider>().ShouldHaveSingleItem();
        shell.FindComponents<CoworkeeAppBar>().ShouldBeEmpty();
        shell.FindComponents<CoworkeeNavMenu>().ShouldBeEmpty();
        shell.FindComponents<SettingsLiveUpdates>().ShouldHaveSingleItem();
    }

    [Fact]
    public void The_shell_shows_toasts_of_signed_in_users_unless_the_app_turns_them_off()
    {
        AddAuthorization().SetAuthorized("Ada");
        var shell = RenderShell();
        shell.WaitForAssertion(() => shell.FindComponents<ToastNotifier>().ShouldHaveSingleItem());

        Services.GetRequiredService<CoworkeeClientOptions>().ShowToasts = false;
        var without = RenderShell();
        without.WaitForAssertion(() => without.Find("#own"));
        without.FindComponents<ToastNotifier>().ShouldBeEmpty();
    }

    [Fact]
    public void A_fixed_theme_and_colour_mode_win_over_the_users_choice()
    {
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string?> { [Contracts.Theming.ThemeSettings.Mode] = "dark" });
        AddAuthorization().SetAuthorized("Ada");
        var theme = new MudTheme { PaletteLight = new PaletteLight { Primary = "#002c61" } };

        var shell = RenderShell(p => p.Add(s => s.Theme, theme).Add(s => s.DarkMode, false));

        shell.WaitForAssertion(() => shell.Find("#own").TextContent.ShouldContain("dark=False"));
        var provider = shell.FindComponent<MudThemeProvider>().Instance;
        provider.Theme.ShouldBeSameAs(theme);
        provider.IsDarkMode.ShouldBeFalse();
    }

    [Fact]
    public void Without_anonymous_access_visitors_go_to_the_sign_in_before_the_layout_renders()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowAnonymous = false });
        AddAuthorization();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/shipments");

        var shell = RenderShell();

        shell.WaitForAssertion(() => nav.Uri.ShouldBe("http://localhost/bff/login?returnUrl=%2Fshipments"));
        shell.Markup.ShouldNotContain("own layout");
    }
}

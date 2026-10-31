using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LayoutTests : ClientTestBase
{
    public LayoutTests()
    {
        Services.AddSingleton<INavigationContributor, AdminNavigation>();
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
    }

    [Fact]
    public async Task Navigation_shows_only_permitted_items()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Ada");
        auth.SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Users.View));

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("/admin/users"));
        layout.Markup.ShouldNotContain("/admin/roles");
    }

    [Fact]
    public async Task Sign_out_navigates_to_the_identity_provider_end_session_url()
    {
        Api.LogoutAsync(Arg.Any<CancellationToken>()).Returns(new BffLogoutDto("https://auth.test/connect/endsession?x=1"));
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await layout.Find("button[aria-label='Account']").ClickAsync(new());
        await layout.WaitForElements(".mud-menu-item").Single(i => i.TextContent.Contains("Sign out")).ClickAsync(new());

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("https://auth.test/connect/endsession?x=1");
    }

    [Fact]
    public void Without_anonymous_access_visitors_go_straight_to_the_sign_in()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowAnonymous = false });
        AddAuthorization();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/products?page=2");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "secret body"))));

        layout.WaitForAssertion(() => nav.Uri.ShouldBe("http://localhost/bff/login?returnUrl=%2Fproducts%3Fpage%3D2"));
        layout.Markup.ShouldNotContain("secret body");
    }

    [Fact]
    public void Without_anonymous_access_signed_in_users_see_the_page()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AllowAnonymous = false });
        AddAuthorization().SetAuthorized("Ada");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "secret body"))));

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("secret body"));
    }

    [Fact]
    public void Logo_is_rendered_as_an_image_never_as_markup()
    {
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(new Coworkee.Contracts.Theming.ThemeDto(
            Guid.CreateVersion7(), "Brand", false, true, System.Text.Json.JsonSerializer.SerializeToElement(new { }), System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            null, null, "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"4\"/></svg>", null, 1));
        AddAuthorization();

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Find("img[data-testid='logo']").GetAttribute("src")!.ShouldStartWith("data:image/svg+xml;base64,"));
        layout.Markup.ShouldNotContain("<circle");
    }

    [Theory]
    [InlineData("data:image/png;base64,AQID")]
    [InlineData("data:image/webp;base64,AQID")]
    public void An_image_logo_is_shown_and_becomes_the_favicon(string logo)
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(Theme(logo));
        AddAuthorization();

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Find("img[data-testid='logo']").GetAttribute("src").ShouldBe(logo));
        layout.WaitForAssertion(() => module.Invocations["setFavicon"].Single().Arguments.ShouldBe([logo]));
    }

    [Fact]
    public void Without_a_theme_logo_the_page_keeps_its_favicon()
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(Theme(null));
        AddAuthorization();

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("body"));
        module.Invocations["setFavicon"].ShouldBeEmpty();
    }

    [Fact]
    public void The_head_script_is_added_once_when_it_is_on()
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, string?>
        {
            [Coworkee.Contracts.Settings.HeadScriptSettings.Enabled] = "true",
            [Coworkee.Contracts.Settings.HeadScriptSettings.Value] = "<script>window.tag = 1;</script>",
        });
        AddAuthorization().SetAuthorized("Ada");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => module.Invocations["injectHeadScript"].Single().Arguments.ShouldBe(["<script>window.tag = 1;</script>"]));
    }

    [Theory]
    [InlineData("false", "<script>window.tag = 1;</script>")]
    [InlineData("true", "")]
    [InlineData(null, null)]
    public void The_head_script_stays_out_while_it_is_off_or_empty(string? enabled, string? value)
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        Api.GetClientSettingsAsync(Arg.Any<CancellationToken>()).Returns(enabled is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>
        {
            [Coworkee.Contracts.Settings.HeadScriptSettings.Enabled] = enabled,
            [Coworkee.Contracts.Settings.HeadScriptSettings.Value] = value,
        });
        AddAuthorization().SetAuthorized("Ada");

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("body"));
        module.Invocations["injectHeadScript"].ShouldBeEmpty();
    }

    private static Coworkee.Contracts.Theming.ThemeDto Theme(string? logo) => new(
        Guid.CreateVersion7(), "Brand", false, true, System.Text.Json.JsonSerializer.SerializeToElement(new { }), System.Text.Json.JsonSerializer.SerializeToElement(new { }),
        null, null, logo, null, 1);

    [Fact]
    public void Uninitialized_system_redirects_to_setup()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(false));
        AddAuthorization();

        Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/setup");
    }
}

using System.Net;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;
using Coworkee.Theming;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Tests;

public sealed class AuthDesignTests(AuthApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    private AuthServerOptions Options => app.App.Services.GetRequiredService<IOptions<AuthServerOptions>>().Value;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await ForgetBrandingAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Options.Design = new SignInDesign();
        Options.AllowThemeDesign = true;
        await ForgetBrandingAsync();
    }

    [Fact]
    public async Task The_account_pages_use_the_card_with_the_theme_colors_font_and_radius_and_load_their_assets()
    {
        var html = await GetAsync("/Account/Login");

        html.ShouldContain("data-design=\"card\"");
        html.ShouldContain("class=\"layout-card\"");
        html.ShouldContain("--radius:6px;");
        html.ShouldContain("--font:Inter, \"Segoe UI\"");
        html.ShouldContain(":root[data-mode=dark]{");
        html.ShouldContain($"/auth-assets/auth.js?v={AuthAssets.Version}");
        html.ShouldContain("data-sign-in=\"password\"");

        using var script = await app.Browser().GetAsync(AuthAssets.Url("auth.js"), Ct);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
        script.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        (await app.Browser().GetAsync(AuthAssets.Url("auth.css"), Ct)).Content.Headers.ContentType!.MediaType.ShouldBe("text/css");
        (await app.Browser().GetAsync("/auth-assets/other.js", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_color_mode_setting_fixes_light_or_dark()
    {
        (await GetAsync("/Account/Login")).ShouldContain("data-mode=\"system\"");

        await app.SetSettingAsync(ThemeSettings.Mode, "dark");
        await app.App.Services.GetRequiredService<HybridCache>().RemoveByTagAsync("coworkee:settings", Ct);

        (await GetAsync("/Account/Login")).ShouldContain("data-mode=\"dark\"");
    }

    [Fact]
    public async Task The_configuration_chooses_the_split_layout_with_its_image_on_the_right()
    {
        Options.Design = new SignInDesign { Layout = SignInLayouts.Split, MediaUrl = "/media/welcome.jpg", MediaSide = SignInMediaSide.Right };

        var html = await GetAsync("/Account/ForgotPassword");

        html.ShouldContain("class=\"split media-right\"");
        html.ShouldContain("<img src=\"/media/welcome.jpg\"");
    }

    [Fact]
    public async Task The_theme_overrides_the_design_and_the_slide_panel_changes_sides_for_the_registration()
    {
        await app.SetSettingAsync("Account.AllowRegistration", "true");
        await app.App.Services.GetRequiredService<HybridCache>().RemoveByTagAsync("coworkee:settings", Ct);
        await UseThemeAsync(new { Layout = "Slide", MediaUrl = "https://media.test/welcome.mp4", MediaSide = "Left" });

        using var response = await app.Browser().GetAsync("/Account/Login", Ct);
        var login = await response.Content.ReadAsStringAsync(Ct);
        var register = await GetAsync("/Account/Register");

        login.ShouldContain("class=\"slide media-left");
        login.ShouldContain("<video src=\"https://media.test/welcome.mp4\"");
        login.ShouldContain("data-testid=\"slide-register\"");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("media-src 'self' https://media.test;");
        register.ShouldContain("class=\"slide media-right");
        register.ShouldContain("data-testid=\"slide-sign-in\"");
    }

    [Fact]
    public async Task Without_permission_for_themes_and_for_unknown_layouts_the_configured_design_stays()
    {
        Options.Design = new SignInDesign { Layout = SignInLayouts.Split };
        Options.AllowThemeDesign = false;
        await UseThemeAsync(new { Layout = "Slide" });

        (await GetAsync("/Account/Login")).ShouldContain("data-design=\"split\"");

        Options.AllowThemeDesign = true;
        await UseThemeAsync(new { Layout = "Missing" }, "Other");
        (await GetAsync("/Account/Login")).ShouldContain("data-design=\"card\"");
    }

    [Fact]
    public void Layout_names_and_media_addresses_that_could_escape_are_dropped()
    {
        var design = AuthBrandingProvider.Checked(new SignInDesign { Layout = "../Secret", MediaUrl = "javascript:alert(1)" });
        AuthBrandingProvider.Checked(new SignInDesign { MediaUrl = "//evil.test/x.png" }).MediaUrl.ShouldBeNull();

        design.Layout.ShouldBeNull();
        design.MediaUrl.ShouldBeNull();
        new AuthBranding("App", null, null, new SignInDesign { MediaUrl = "/v/intro.webm?x=1" }).MediaIsVideo.ShouldBeTrue();
    }

    [Fact]
    public void A_theme_design_fills_only_what_it_sets()
    {
        var options = JsonSerializer.SerializeToElement(new { Dense = true, SignIn = new { MediaSide = "Right" } });

        var design = new SignInDesign { Layout = "Split", MediaUrl = "/a.jpg" }.With(AuthBrandingProvider.ThemeDesign(options));

        design.Layout.ShouldBe("Split");
        design.MediaUrl.ShouldBe("/a.jpg");
        design.MediaSide.ShouldBe(SignInMediaSide.Right);
        AuthBrandingProvider.ThemeDesign(JsonSerializer.SerializeToElement(new { SignIn = new { MediaSide = "Up" } })).ShouldBeNull();
    }

    private async Task UseThemeAsync(object signIn, string name = "Brand")
    {
        var palette = JsonSerializer.SerializeToElement(new { Primary = "#123456" });
        var created = await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateTheme(new ThemeRequest(name, palette, palette, null, null, null, null,
            Options: JsonSerializer.SerializeToElement(new { SignIn = signIn }))));
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SetDefaultTheme(created.Value.Id))).IsSuccess.ShouldBeTrue();
        await ForgetBrandingAsync();
    }

    private async Task<string> GetAsync(string path)
    {
        using var response = await app.Browser().GetAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private ValueTask ForgetBrandingAsync() => app.App.Services.GetRequiredService<HybridCache>().RemoveAsync("coworkee:auth-branding", Ct);
}

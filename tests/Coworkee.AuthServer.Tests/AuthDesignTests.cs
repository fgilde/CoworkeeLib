using System.Net;
using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;
using Coworkee.Settings;
using Coworkee.Theming;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AuthServer.Tests;

public sealed class AuthDesignTests(AuthApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    private AuthServerOptions Options => app.App.Services.GetRequiredService<IOptionsMonitor<AuthServerOptions>>().CurrentValue;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await ForgetBrandingAsync();
    }

    public async ValueTask DisposeAsync()
    {
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new ResetAppConfiguration(SignInDesign.Section))).IsSuccess.ShouldBeTrue();
        Options.Design = new SignInDesign();
        await ForgetBrandingAsync();
    }

    [Fact]
    public async Task The_account_pages_slide_by_default_with_the_theme_colors_font_and_radius_and_load_their_assets()
    {
        var html = await GetAsync("/Account/Login");

        html.ShouldContain("data-design=\"slide\"");
        html.ShouldContain("class=\"layout-slide\"");
        html.ShouldContain("--radius:14px;");
        html.ShouldContain("--font:\"Segoe UI\", Tahoma");
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
    public async Task Administrators_change_the_design_for_the_whole_installation_and_the_slide_panel_changes_sides_for_the_registration()
    {
        await app.SetSettingAsync("Account.AllowRegistration", "true");
        await app.App.Services.GetRequiredService<HybridCache>().RemoveByTagAsync("coworkee:settings", Ct);
        await UseThemeAsync();
        var saved = await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SaveAppConfiguration(SignInDesign.Section,
            JsonSerializer.SerializeToElement(new SignInDesign { Layout = "Slide", MediaUrl = "https://media.test/welcome.mp4", MediaSide = SignInMediaSide.Left })));
        saved.IsSuccess.ShouldBeTrue();

        using var response = await app.Browser().GetAsync("/Account/Login", Ct);
        var login = await response.Content.ReadAsStringAsync(Ct);
        var register = await GetAsync("/Account/Register");

        login.ShouldContain("class=\"slide media-left");
        login.ShouldContain("<video src=\"https://media.test/welcome.mp4\"");
        login.ShouldContain("data-testid=\"slide-register\"");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("media-src 'self' https://media.test;");
        register.ShouldContain("class=\"slide media-right");
        register.ShouldContain("data-testid=\"slide-sign-in\"");
        saved.Value.Values.GetProperty("mediaSide").GetString().ShouldBe("Left");
    }

    [Fact]
    public async Task An_unknown_layout_falls_back_to_the_default()
    {
        Options.Design = new SignInDesign { Layout = "Missing" };

        (await GetAsync("/Account/Login")).ShouldContain("data-design=\"slide\"");
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

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=", "data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>", "data:image/svg&#x2B;xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjwvc3ZnPg==")]
    public async Task The_account_pages_show_the_logo_of_the_default_theme_as_data_url(string logo, string shown)
    {
        var palette = JsonSerializer.SerializeToElement(new { Primary = "#123456" });
        var created = await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateTheme(new ThemeRequest("Logo", palette, palette, null, null, logo, null)));
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SetDefaultTheme(created.Value.Id))).IsSuccess.ShouldBeTrue();
        await ForgetBrandingAsync();

        (await GetAsync("/Account/Login")).ShouldContain($"<img src=\"{shown}\"");
    }

    // a theme that still carries the former per theme design: it no longer counts
    private async Task UseThemeAsync()
    {
        var palette = JsonSerializer.SerializeToElement(new { Primary = "#123456" });
        var created = await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateTheme(new ThemeRequest("Brand", palette, palette, null, null, null, null,
            Options: JsonSerializer.SerializeToElement(new { SignIn = new { Layout = "Split" } }))));
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

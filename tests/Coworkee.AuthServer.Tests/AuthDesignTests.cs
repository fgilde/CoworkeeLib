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

    [Theory]
    [InlineData(SignInLayouts.Card, "https://media.test/sky.webm", 40)]
    [InlineData(SignInLayouts.Split, "https://media.test/sky.jpg", null)]
    [InlineData(SignInLayouts.Slide, "https://media.test/sky.jpg", 200)]
    public async Task The_page_background_lies_behind_every_layout_independent_of_the_panel_media(string layout, string background, int? dim)
    {
        Options.Design = new SignInDesign { Layout = layout, MediaUrl = "/media/welcome.jpg", BackgroundUrl = background, BackgroundDim = dim };

        using var response = await app.Browser().GetAsync("/Account/Login", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        html.ShouldContain($"class=\"layout-{layout.ToLowerInvariant()} has-background\"");
        html.ShouldContain($"style=\"--dim:{Math.Clamp(dim ?? 0, 0, 90)}%\" data-testid=\"design-background\"");
        html.ShouldContain(background.EndsWith(".webm", StringComparison.Ordinal)
            ? $"<video src=\"{background}\" autoplay muted loop playsinline></video>"
            : $"<img src=\"{background}\" alt=\"\" />");
        if (layout != SignInLayouts.Card)
        {
            html.ShouldContain("<img src=\"/media/welcome.jpg\" alt=\"\" data-testid=\"design-media\"");
        }

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("img-src 'self' data: https://media.test");
        policy.ShouldContain("media-src 'self' https://media.test;");
        policy.ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task A_stored_design_without_the_page_background_looks_as_before()
    {
        var saved = await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new SaveAppConfiguration(SignInDesign.Section,
            JsonDocument.Parse("""{"layout":"Split","mediaUrl":"/media/welcome.jpg"}""").RootElement));
        saved.IsSuccess.ShouldBeTrue();

        var html = await GetAsync("/Account/Login");

        html.ShouldContain("class=\"layout-split\"");
        html.ShouldContain("class=\"split media-left\"");
        html.ShouldNotContain("design-background");
    }

    [Fact]
    public async Task The_preview_renders_the_real_sign_in_page_with_an_unsaved_design_only_inside_a_frame_of_a_client()
    {
        Options.Design = new SignInDesign { Layout = SignInLayouts.Card };
        var design = Uri.EscapeDataString("""{"layout":"Slide","mediaSide":"Left","backgroundUrl":"https://media.test/sky.jpg","backgroundDim":30}""");
        using var request = new HttpRequestMessage(HttpMethod.Get, SignInPreview.Path + "?design=" + design);
        request.Headers.Add("Sec-Fetch-Dest", "iframe");

        using var response = await app.Browser().SendAsync(request, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("class=\"slide media-left");
        html.ShouldContain("data-sign-in=\"password\"");
        html.ShouldContain("<img src=\"https://media.test/sky.jpg\" alt=\"\" />");
        html.ShouldContain("--dim:30%");
        response.Headers.Contains("X-Frame-Options").ShouldBeFalse();
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("frame-ancestors https://client.test");
        policy.ShouldContain("img-src 'self' data: https://media.test");
        // the saved design is untouched and links keep the sign-in address
        (await GetAsync("/Account/Login")).ShouldContain("data-design=\"card\"");
        (await GetAsync("/Account/ForgotPassword")).ShouldNotContain(SignInPreview.Path);
    }

    [Theory]
    [InlineData("GET", null, "{\"layout\":\"Card\"}")]
    [InlineData("GET", "document", "{\"layout\":\"Card\"}")]
    [InlineData("POST", "iframe", "{\"layout\":\"Card\"}")]
    [InlineData("GET", "iframe", "not json")]
    [InlineData("GET", "iframe", "")]
    public async Task The_preview_answers_nothing_to_links_posts_and_broken_designs(string method, string? destination, string design)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), SignInPreview.Path + "?design=" + Uri.EscapeDataString(design));
        if (destination is not null)
        {
            request.Headers.Add("Sec-Fetch-Dest", destination);
        }

        using var response = await app.Browser().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
        AuthBrandingProvider.Checked(new SignInDesign { BackgroundUrl = "javascript:alert(1)" }).BackgroundUrl.ShouldBeNull();
        AuthBrandingProvider.Checked(new SignInDesign { BackgroundUrl = "/\\evil.test/x.png" }).BackgroundUrl.ShouldBeNull();
        AuthBrandingProvider.Checked(new SignInDesign { BackgroundUrl = "data:image/png;base64,AA" }).BackgroundUrl.ShouldBeNull();
        AuthBrandingProvider.Checked(new SignInDesign { BackgroundUrl = "/media/sky.jpg", BackgroundDim = -5 }).ShouldSatisfyAllConditions(
            d => d.BackgroundUrl.ShouldBe("/media/sky.jpg"), d => d.BackgroundDim.ShouldBe(0));
        new AuthBranding("App", null, null, new SignInDesign { BackgroundUrl = "/v/sky.MP4" }).BackgroundIsVideo.ShouldBeTrue();
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

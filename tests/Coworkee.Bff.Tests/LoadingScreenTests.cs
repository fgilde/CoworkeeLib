using System.Net;
using System.Text.Json;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Bff.Tests;

public sealed class LoadingScreenTests
{
    private static readonly string Logo = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";

    private static LoadingScreenDto Screen(LoadingScreenBackground background = LoadingScreenBackground.Animated, string? media = null, string primary = "#123456", bool signedIn = false) => new(
        new ThemeDto(Guid.NewGuid(), "Brand", false, true, Palette(primary, "#fafafa"), Palette("#abcdef", "#101010"), null, null, Logo, null, 1),
        background,
        media,
        signedIn);

    private static JsonElement Palette(string primary, string background) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = primary, ["Background"] = background });

    [Fact]
    public void Without_a_mode_both_palettes_follow_the_system_preference()
    {
        var html = LoadingScreenMarkup.Render(Screen(), null, null);

        html.ShouldContain("#coworkee-loading{--cwl-primary:#123456;");
        html.ShouldContain("--cwl-bg:#fafafa;");
        html.ShouldContain("@media (prefers-color-scheme: dark){#coworkee-loading{--cwl-primary:#abcdef;");
        html.ShouldContain("src=\"/bff/loading-screen/logo?v=");
        html.ShouldContain("data-background=\"animated\"");
        html.ShouldContain("class=\"cwl-blob");
    }

    [Fact]
    public void A_remembered_dark_mode_renders_the_dark_palette_only()
    {
        var html = LoadingScreenMarkup.Render(Screen(), "dark", null);

        html.ShouldContain("#coworkee-loading{--cwl-primary:#abcdef;");
        html.ShouldNotContain("#123456");
        html.ShouldNotContain("prefers-color-scheme: dark");
    }

    [Fact]
    public void Colours_that_are_no_colours_fall_back_to_neutral_ones()
    {
        var html = LoadingScreenMarkup.Render(Screen(primary: "red;}</style><script>alert(1)</script>"), "light", null);

        html.ShouldNotContain("alert(1)");
        html.ShouldContain("--cwl-primary:#594ae2;");
    }

    [Theory]
    [InlineData("https://web.test/api/v1/files/public/1/hero.mp4", "<video class=\"cwl-media\"")]
    [InlineData("/media/hero.jpg", "<img class=\"cwl-media\"")]
    public void Sign_in_media_is_shown_under_a_veil(string media, string element)
    {
        var html = LoadingScreenMarkup.Render(Screen(LoadingScreenBackground.SignInMedia, media), null, null);

        html.ShouldContain(element);
        html.ShouldContain("class=\"cwl-veil");
        html.ShouldContain("data-background=\"media\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.test/x.mp4")]
    public void Sign_in_media_without_a_safe_address_falls_back_to_the_animation(string? media)
    {
        var html = LoadingScreenMarkup.Render(Screen(LoadingScreenBackground.SignInMedia, media), null, null);

        html.ShouldContain("data-background=\"animated\"");
        html.ShouldNotContain("class=\"cwl-media");
    }

    [Fact]
    public void Plain_has_neither_shapes_nor_media()
    {
        var html = LoadingScreenMarkup.Render(Screen(LoadingScreenBackground.Plain, "/media/hero.jpg"), null, null);

        html.ShouldNotContain("class=\"cwl-blob");
        html.ShouldNotContain("class=\"cwl-media");
    }

    [Fact]
    public void Without_an_answer_of_the_api_the_screen_uses_the_app_logo_and_neutral_colours()
    {
        var html = LoadingScreenMarkup.Render(null, null, "coworkee-icon.svg");

        html.ShouldContain("src=\"coworkee-icon.svg\"");
        html.ShouldContain("--cwl-primary:#594ae2;");
        html.ShouldContain("<script src=\"/bff/loading-screen/script.js?v=");
    }

    [Fact]
    public async Task The_source_forwards_the_api_answer_and_falls_back_when_the_api_fails()
    {
        var handler = new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(), JsonSerializerOptions.Web));
        await using var app = await StartAsync(handler);
        var source = app.Services.GetRequiredService<LoadingScreenSource>();

        var screen = await source.GetAsync(new DefaultHttpContext { RequestServices = app.Services });

        screen!.Theme!.Name.ShouldBe("Brand");
        handler.Requests.ShouldHaveSingleItem().ShouldBe("http://api.test/api/v1/themes/loading-screen");

        await using var failing = await StartAsync(new Answering(HttpStatusCode.InternalServerError, string.Empty));
        (await failing.Services.GetRequiredService<LoadingScreenSource>().GetAsync(new DefaultHttpContext { RequestServices = failing.Services })).ShouldBeNull();
    }

    [Fact]
    public async Task The_component_renders_the_screen_in_the_remembered_mode_and_language()
    {
        var handler = new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(), JsonSerializerOptions.Web), German);
        await using var app = await StartAsync(handler);
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Headers.Cookie = $"{LoadingScreenPreferences.Cookie}=mode=dark&theme={Guid.NewGuid()}&culture=de";

        var html = await RenderAsync(app, context);

        html.ShouldContain("id=\"coworkee-loading\"");
        html.ShouldContain("--cwl-primary:#abcdef;");
        html.ShouldContain("lang=\"de\"");
        html.ShouldContain("aria-label=\"Wird geladen\"");
        html.ShouldContain(">Neu laden</button>");
        html.ShouldContain("id=\"blazor-error-ui\"");
        // anonymous: the chosen theme is not asked for
        handler.Requests.ShouldContain("http://api.test/api/v1/themes/loading-screen");
    }

    [Fact]
    public async Task A_signed_in_visitor_asks_for_the_chosen_theme_and_the_answers_are_cached_per_organisation_and_theme()
    {
        var handler = new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(signedIn: true), JsonSerializerOptions.Web), German);
        await using var app = await StartAsync(handler);
        var source = app.Services.GetRequiredService<LoadingScreenSource>();
        var theme = Guid.NewGuid();
        HttpContext SignedIn(string tenant) => new DefaultHttpContext
        {
            RequestServices = app.Services,
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("tenant", tenant)], "test")),
        };

        await source.GetAsync(SignedIn("a"), theme);
        await source.GetAsync(SignedIn("a"), theme);
        await source.GetAsync(SignedIn("b"), theme);
        await source.GetAsync(new DefaultHttpContext { RequestServices = app.Services }, theme);

        handler.Requests.Count(r => r.EndsWith($"?themeId={theme}", StringComparison.Ordinal)).ShouldBe(2);
        handler.Requests.ShouldContain("http://api.test/api/v1/themes/loading-screen");
    }

    [Theory]
    [InlineData(null, "fr-CH,fr;q=0.9,en;q=0.5", "fr-CH")]
    [InlineData("mode=light&culture=<script>", "de", "de")]
    [InlineData(null, null, "en")]
    public void The_language_comes_from_the_cookie_else_from_the_browser(string? cookie, string? acceptLanguage, string culture)
    {
        var context = new DefaultHttpContext();
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{LoadingScreenPreferences.Cookie}={cookie}";
        }

        if (acceptLanguage is not null)
        {
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        LoadingScreenPreferences.Read(context.Request).Culture.ShouldBe(culture);
    }

    [Fact]
    public void A_failed_start_has_a_reload_button_and_details_in_the_same_screen()
    {
        var html = LoadingScreenMarkup.Render(Screen(), null, null, LoadingScreenTexts.From("de", German));

        html.ShouldContain("class=\"cwl-error\"");
        html.ShouldContain(">Die App konnte nicht starten.</div>");
        html.ShouldContain("<summary>Details</summary>");
        html.ShouldContain("class=\"cwl-reload\"");
    }

    [Fact]
    public async Task The_script_is_its_own_cacheable_file_so_a_strict_content_security_policy_runs_it()
    {
        await using var app = await StartAsync(new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(LoadingScreenBackground.SignInMedia, "/media/a.mp4"), JsonSerializerOptions.Web)));
        var page = LoadingScreenMarkup.Render(Screen(LoadingScreenBackground.SignInMedia, "/media/a.mp4"), null, null);
        var url = System.Text.RegularExpressions.Regex.Match(page, "<script src=\"(/bff/loading-screen/script[.]js[^\"]+)\"></script>").Groups[1].Value;

        using var response = await app.GetTestClient().GetAsync(url, TestContext.Current.CancellationToken);

        page.ShouldNotContain("<script>");
        page.ShouldNotContain("onclick=");
        page.ShouldNotContain("onerror=");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        response.Headers.CacheControl!.ToString().ShouldContain("immutable");
        var script = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        script.ShouldContain("location.reload()");
        script.ShouldContain("fail(");
    }

    [Fact]
    public async Task Any_number_of_browser_languages_maps_to_the_active_languages_with_a_bounded_number_of_api_calls()
    {
        var handler = new Answering(HttpStatusCode.OK, "{}", German);
        await using var app = await StartAsync(handler);
        var source = app.Services.GetRequiredService<LoadingScreenSource>();

        for (var i = 0; i < 1000; i++)
        {
            var texts = await source.TextsAsync(new DefaultHttpContext { RequestServices = app.Services }, i % 2 == 0 ? $"de-X{i}" : $"xx-Y{i}");
            texts.Culture.ShouldBe(i % 2 == 0 ? "de" : "en");
        }

        handler.Requests.Count.ShouldBe(2);
        handler.Requests.ShouldBe(["http://api.test/api/v1/localization/languages", "http://api.test/api/v1/localization/texts/de"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("ZH-hant", "zh-Hant")]
    [InlineData("zh-CN", "zh")]
    [InlineData("fr", null)]
    public void The_language_is_the_active_one_or_its_parent(string culture, string? active) =>
        LoadingScreenSource.Active([new(null, "zh", "中文", true, false), new(null, "zh-Hant", "繁體中文", true, false)], culture).ShouldBe(active);

    [Fact]
    public async Task A_visitor_who_leaves_does_not_spoil_the_shared_answer()
    {
        var handler = new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(), JsonSerializerOptions.Web));
        await using var app = await StartAsync(handler);
        var source = app.Services.GetRequiredService<LoadingScreenSource>();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        (await source.GetAsync(new DefaultHttpContext { RequestServices = app.Services, RequestAborted = aborted.Token })).ShouldNotBeNull();
        (await source.GetAsync(new DefaultHttpContext { RequestServices = app.Services }))!.Theme!.Name.ShouldBe("Brand");
        handler.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_answer_for_a_token_the_api_did_not_accept_is_not_cached_for_the_organisation()
    {
        var handler = new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(signedIn: false), JsonSerializerOptions.Web));
        await using var app = await StartAsync(handler);
        var source = app.Services.GetRequiredService<LoadingScreenSource>();
        HttpContext SignedIn() => new DefaultHttpContext
        {
            RequestServices = app.Services,
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new("tenant", "a")], "test")),
        };

        await source.GetAsync(SignedIn());
        await source.GetAsync(SignedIn());

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_logo_is_its_own_cacheable_response_that_runs_no_script()
    {
        await using var app = await StartAsync(new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(), JsonSerializerOptions.Web)));
        var client = app.GetTestClient();
        var page = LoadingScreenMarkup.Render(await app.Services.GetRequiredService<LoadingScreenSource>().GetAsync(new DefaultHttpContext { RequestServices = app.Services }), null, null);
        var url = System.Text.RegularExpressions.Regex.Match(page, "src=\"(/bff/loading-screen/logo[^\"]+)\"").Groups[1].Value;

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(Logo);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        response.Headers.CacheControl!.ToString().ShouldContain("immutable");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
        page.ShouldNotContain("base64");

        using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        (await client.SendAsync(conditional, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task The_page_with_the_loading_screen_is_not_kept_by_shared_caches()
    {
        await using var app = await StartAsync(new Answering(HttpStatusCode.OK, JsonSerializer.Serialize(Screen(), JsonSerializerOptions.Web)));
        var context = new DefaultHttpContext { RequestServices = app.Services };

        await RenderAsync(app, context);

        context.Response.Headers.CacheControl.ToString().ShouldBe("no-cache, no-store");
    }

    private static readonly Contracts.Localization.LanguageDto[] Languages = [new(null, "en", "English", true, true), new(null, "de", "Deutsch", true, false)];

    private static readonly Dictionary<string, string> German = new()
    {
        ["Loading"] = "Wird geladen",
        ["The app could not start."] = "Die App konnte nicht starten.",
        ["Reload"] = "Neu laden",
    };

    private static async Task<string> RenderAsync(WebApplication app, HttpContext context)
    {
        await using var renderer = new HtmlRenderer(app.Services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(Host.Context)] = context }))).ToHtmlString());
    }

    private static async Task<WebApplication> StartAsync(HttpMessageHandler handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:Bff:Authority"] = "https://auth.test";
        builder.Configuration["Coworkee:Bff:ApiAddress"] = "http://api.test";
        builder.Configuration["Coworkee:Bff:ClientId"] = "web";
        builder.AddCoworkeeBff();
        builder.Services.AddHttpClient(LoadingScreenSource.Client).ConfigurePrimaryHttpMessageHandler(() => handler);
        var app = builder.Build();
        app.MapCoworkeeBff();
        await app.StartAsync();
        return app;
    }

    private sealed class Answering(HttpStatusCode status, string body, Dictionary<string, string>? texts = null) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add(url);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var json = url.Contains("/localization/texts/", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new Contracts.Localization.TextsDto("de", texts ?? []), JsonSerializerOptions.Web)
                : url.EndsWith("/localization/languages", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(Languages, JsonSerializerOptions.Web)
                    : body;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Host : ComponentBase
    {
        [Parameter] public HttpContext Context { get; set; } = null!;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<HttpContext>>(0);
            builder.AddComponentParameter(1, nameof(CascadingValue<HttpContext>.Value), Context);
            builder.AddComponentParameter(2, nameof(CascadingValue<HttpContext>.ChildContent), (RenderFragment)(child =>
            {
                child.OpenComponent<CoworkeeLoadingScreen>(0);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}

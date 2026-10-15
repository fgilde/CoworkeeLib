using System.Net;
using System.Text.Json;
using Coworkee.Bff.Pwa;
using Coworkee.Contracts.Theming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Bff.Tests;

public sealed class PwaTests
{
    private static readonly LoadingScreenDto Screen = new(
        new ThemeDto(Guid.NewGuid(), "Brand", false, true,
            JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456", ["Background"] = "#fafafa", ["AppbarBackground"] = "#0b3d91" }),
            JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#abcdef" }), null, null, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>", null, 1),
        LoadingScreenBackground.Animated, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_manifest_carries_the_app_name_and_the_colours_and_logo_of_the_theme()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/manifest.webmanifest", Ct);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/manifest+json");
        var manifest = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        manifest.GetProperty("name").GetString().ShouldBe("Demo Shop");
        manifest.GetProperty("display").GetString().ShouldBe("standalone");
        manifest.GetProperty("start_url").GetString().ShouldBe("/");
        manifest.GetProperty("theme_color").GetString().ShouldBe("#0b3d91");
        manifest.GetProperty("background_color").GetString().ShouldBe("#fafafa");
        var icon = manifest.GetProperty("icons")[0];
        icon.GetProperty("src").GetString().ShouldBe("/bff/pwa/icon");
        icon.GetProperty("type").GetString().ShouldBe("image/svg+xml");
        (await app.GetTestClient().GetStringAsync("/bff/pwa/icon", Ct)).ShouldContain("<svg");
    }

    [Fact]
    public async Task The_service_worker_only_answers_failed_page_loads_and_waits_for_the_user_to_update()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/service-worker.js", Ct);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/javascript");
        response.Headers.CacheControl!.NoCache.ShouldBeTrue();
        var script = await response.Content.ReadAsStringAsync(Ct);
        script.ShouldNotContain("__VERSION__");
        script.ShouldContain("request.mode === 'navigate' && request.method === 'GET'");
        script.ShouldNotContain("cache.put");
        script.ShouldContain("event.data === 'skip-waiting'");
        System.Text.RegularExpressions.Regex.Count(script, "skipWaiting").ShouldBe(1);
        script.ShouldContain("notificationclick");
    }

    [Fact]
    public async Task The_offline_page_shows_the_theme_and_a_reload_link()
    {
        await using var app = await StartAsync();

        var page = await app.GetTestClient().GetStringAsync("/bff/pwa/offline", Ct);

        page.ShouldContain("#123456");
        page.ShouldContain("data:image/svg+xml;base64,");
        page.ShouldContain("<a href=\"\">Reload</a>");
        page.ShouldNotContain("<script");
    }

    [Fact]
    public async Task The_head_component_links_the_manifest_and_colours_the_title_bar()
    {
        await using var app = await StartAsync();
        await using var renderer = new HtmlRenderer(app.Services, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<Host>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(Host.Context)] = new DefaultHttpContext { RequestServices = app.Services } }))).ToHtmlString());

        html.ShouldContain("<link rel=\"manifest\" href=\"/manifest.webmanifest\" crossorigin=\"use-credentials\" />");
        html.ShouldContain("<meta name=\"theme-color\" media=\"(prefers-color-scheme: light)\" content=\"#0b3d91\" />");
        html.ShouldContain("<meta name=\"theme-color\" media=\"(prefers-color-scheme: dark)\" content=\"#abcdef\" />");
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Coworkee:Bff:Authority"] = "https://auth.test";
        builder.Configuration["Coworkee:Bff:ApiAddress"] = "http://api.test";
        builder.Configuration["Coworkee:Bff:ClientId"] = "web";
        builder.Configuration["Coworkee:Bff:Pwa:Name"] = "Demo Shop";
        builder.AddCoworkeeBff();
        builder.Services.AddHttpClient(LoadingScreenSource.Client).ConfigurePrimaryHttpMessageHandler(() => new Answering());
        var app = builder.Build();
        app.MapCoworkeeBff();
        await app.StartAsync();
        return app;
    }

    private sealed class Answering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.Contains("/localization/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Screen, JsonSerializerOptions.Web), System.Text.Encoding.UTF8, "application/json") });
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
                child.OpenComponent<CoworkeePwa>(0);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}

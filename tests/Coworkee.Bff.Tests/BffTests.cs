using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Bff.Tests;

public sealed class BffTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
            ["Coworkee:Bff:AuthorizationEndpoint"] = "https://auth.test/connect/authorize",
            ["Coworkee:Bff:ForwardedPrefixes:0"] = "/admin/jobs",
            ["Coworkee:Bff:FrameAncestors:0"] = "https://desk.test/",
            ["Coworkee:Mcp:Path"] = "/ai/mcp",
        });
        builder.AddCoworkeeBff();
        _app = builder.Build();
        _app.MapCoworkeeBff();
        _app.MapGet("/test/page", () => "page").RequireAuthorization("perm:Identity.Roles.View");
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Pages_may_be_framed_by_the_configured_origins_only()
    {
        var response = await _client.GetAsync("/bff/user", TestContext.Current.CancellationToken);

        response.Headers.GetValues("Content-Security-Policy").ShouldContain("frame-ancestors 'self' https://desk.test");
    }

    [Fact]
    public async Task Anonymous_user_endpoint_reports_signed_out()
    {
        var user = await _client.GetFromJsonAsync<BffUserDto>("/bff/user", TestContext.Current.CancellationToken);

        user!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Unsafe_api_call_without_csrf_header_is_rejected() =>
        (await _client.PostAsync("/api/v1/identity/users", new StringContent("{}"), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Unsafe_api_call_with_csrf_header_is_forwarded()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/users") { Content = new StringContent("{}") };
        request.Headers.Add("X-CSRF", "1");

        (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Unsubscribe_form_post_is_forwarded_without_csrf_header() =>
        (await _client.PostAsync("/api/v1/mail/unsubscribe", new FormUrlEncodedContent([new("token", "x")]), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadGateway);

    [Fact]
    public async Task Safe_api_call_is_forwarded_without_csrf_header() =>
        (await _client.GetAsync("/api/v1/system", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);

    [Fact]
    public async Task Mcp_is_forwarded_under_the_configured_path_only()
    {
        (await _client.PostAsync("/ai/mcp", new StringContent("{}"), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await _client.PostAsync("/mcp", new StringContent("{}"), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Configured_prefixes_are_forwarded()
    {
        (await _client.GetAsync("/admin/jobs/recurring", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await _client.GetAsync("/admin/other", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Api_docs_need_a_login_but_the_openapi_document_is_forwarded_anonymously()
    {
        var swagger = await _client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        swagger.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        swagger.Headers.Location!.Host.ShouldBe("auth.test");
        (await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Logout_without_csrf_header_is_rejected() =>
        (await _client.PostAsync("/bff/logout", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Theory]
    [InlineData("/\\evil.test")]
    [InlineData("//evil.test")]
    [InlineData("https://evil.test")]
    public async Task Login_never_redirects_off_site(string returnUrl)
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=" + Uri.EscapeDataString(returnUrl), TestContext.Current.CancellationToken);

        var state = System.Web.HttpUtility.ParseQueryString(response.Headers.Location!.Query)["state"]!;
        var properties = _app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>()
            .Get("OpenIdConnect").StateDataFormat.Unprotect(state)!;
        properties.RedirectUri.ShouldBe("/");
    }

    [Fact]
    public async Task Pages_with_client_permission_policies_challenge_instead_of_failing() =>
        (await _client.GetAsync("/test/page", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

    [Fact]
    public async Task Login_challenges_the_identity_provider()
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=/admin", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public void Session_cookie_keeps_the_default_name_unless_one_is_configured()
    {
        CookieName(_app.Services).ShouldBe(".AspNetCore.Cookies");

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "airworks",
            ["Coworkee:Bff:CookieName"] = "shop-airworks",
        });
        builder.AddCoworkeeBff();

        CookieName(builder.Build().Services).ShouldBe("shop-airworks");
    }

    private static string? CookieName(IServiceProvider services) =>
        services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>().Get("Cookies").Cookie.Name;

    [Fact]
    public async Task Login_passes_the_login_hint_to_the_identity_provider()
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=/admin&loginHint=" + Uri.EscapeDataString("ada@acme.test"), TestContext.Current.CancellationToken);

        response.Headers.Location!.Query.ShouldContain("login_hint=ada%40acme.test");
    }
}

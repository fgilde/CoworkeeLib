using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Bff.Tests;

public sealed class TokenRefreshTests : IAsyncDisposable
{
    private WebApplication? _app;

    [Fact]
    public async Task Expired_session_with_rejected_refresh_signs_out()
    {
        var browser = await StartAsync(new StubTokenEndpoint(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));

        var user = await browser.GetFromJsonAsync<BffUserDto>("/bff/user", TestContext.Current.CancellationToken);

        user!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Expired_session_is_refreshed()
    {
        var endpoint = new StubTokenEndpoint(HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":900}""");
        var browser = await StartAsync(endpoint);

        var user = await browser.GetFromJsonAsync<BffUserDto>("/bff/user", TestContext.Current.CancellationToken);

        user!.IsAuthenticated.ShouldBeTrue();
        endpoint.Calls.ShouldBe(1);
        (await browser.GetStringAsync("/test/token", TestContext.Current.CancellationToken)).ShouldBe("t:new-access");
    }

    [Fact]
    public async Task Logout_ends_the_local_session_and_returns_the_identity_provider_end_session_url()
    {
        var browser = await StartAsync(new StubTokenEndpoint(HttpStatusCode.OK, """{"access_token":"a","refresh_token":"r","expires_in":900}"""));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("X-CSRF", "1");

        var response = await browser.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logout = await response.Content.ReadFromJsonAsync<BffLogoutDto>(TestContext.Current.CancellationToken);
        logout!.Redirect.ShouldStartWith("https://auth.test/connect/endsession");
        (await browser.GetFromJsonAsync<BffUserDto>("/bff/user", TestContext.Current.CancellationToken))!.IsAuthenticated.ShouldBeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task<HttpClient> StartAsync(StubTokenEndpoint endpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Coworkee:Bff:Authority"] = "https://auth.test",
            ["Coworkee:Bff:ClientId"] = "web",
            ["Coworkee:Bff:ApiAddress"] = "http://api.test",
            ["Coworkee:Bff:AuthorizationEndpoint"] = "https://auth.test/connect/authorize",
        });
        builder.AddCoworkeeBff();
        builder.Services.AddHttpClient(nameof(TokenRefresher)).ConfigurePrimaryHttpMessageHandler(() => endpoint);
        _app = builder.Build();
        _app.MapCoworkeeBff();
        _app.MapGet("/test/signin", async (HttpContext context) =>
        {
            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = "old-access" },
                new AuthenticationToken { Name = "refresh_token", Value = "old-refresh" },
                new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture) },
            ]);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString()), new Claim("name", "Ada")], "test", "name", "role")), properties);
        });
        _app.MapGet("/test/token", async (HttpContext context) => "t:" + await context.GetTokenAsync("access_token"));
        await _app.StartAsync();

        var server = _app.GetTestServer();
        var browser = new HttpClient(new CookieContainerHandler { InnerHandler = server.CreateHandler() }) { BaseAddress = server.BaseAddress };
        (await browser.GetAsync("/test/signin", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        return browser;
    }

    private sealed class StubTokenEndpoint(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}

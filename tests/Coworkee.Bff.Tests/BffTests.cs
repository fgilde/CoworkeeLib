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
    public async Task Safe_api_call_is_forwarded_without_csrf_header() =>
        (await _client.GetAsync("/api/v1/system", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);

    [Fact]
    public async Task Configured_prefixes_are_forwarded()
    {
        (await _client.GetAsync("/admin/jobs/recurring", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await _client.GetAsync("/admin/other", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
}

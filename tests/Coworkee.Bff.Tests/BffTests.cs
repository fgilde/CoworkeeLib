using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

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
        });
        builder.AddCoworkeeBff();
        _app = builder.Build();
        _app.MapCoworkeeBff();
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
    public async Task Logout_without_csrf_header_is_rejected() =>
        (await _client.PostAsync("/bff/logout", null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

    [Fact]
    public async Task Login_challenges_the_identity_provider()
    {
        var response = await _client.GetAsync("/bff/login?returnUrl=/admin", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }
}

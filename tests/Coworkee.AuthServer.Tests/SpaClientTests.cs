using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Tests;

public sealed class SpaClientTests(AuthApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_without_secret_is_seeded_public_and_needs_pkce()
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var client = (await manager.FindByClientIdAsync(AuthApp.SpaClientId, Ct))!;

        (await manager.GetClientTypeAsync(client, Ct)).ShouldBe(ClientTypes.Public);
        (await manager.HasRequirementAsync(client, Requirements.Features.ProofKeyForCodeExchange, Ct)).ShouldBeTrue();
        (await manager.GetRedirectUrisAsync(client, Ct)).ShouldBe([AuthApp.SpaUrl]);

        var flow = new OidcFlow(app, AuthApp.SpaClientId, AuthApp.SpaUrl);
        (await flow.LoginAsync("admin@acme.test", "Admin#12345")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var withoutPkce = await flow.Browser.GetAsync($"/connect/authorize?client_id={AuthApp.SpaClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(AuthApp.SpaUrl)}", Ct);
        withoutPkce.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withoutPkce.Headers.Location.ShouldBeNull();
        (await withoutPkce.Content.ReadAsStringAsync(Ct)).ShouldContain("code_challenge");
    }

    [Fact]
    public async Task The_redirect_address_must_match_exactly()
    {
        var flow = new OidcFlow(app, AuthApp.SpaClientId, AuthApp.SpaUrl + "/elsewhere");
        (await flow.LoginAsync("admin@acme.test", "Admin#12345")).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var response = await flow.FollowAsync(await flow.AuthorizeAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task The_alias_scope_gives_tokens_for_the_api_and_a_refresh_without_secret()
    {
        var flow = new OidcFlow(app, AuthApp.SpaClientId, AuthApp.SpaUrl) { Scope = "openid profile email roles offline_access Shop" };

        var tokens = await flow.SignInAsync("admin@acme.test", "Admin#12345");

        var access = new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("access_token").GetString());
        access.Audiences.ShouldBe(["test_api"]);
        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var api = new HttpRequestMessage(HttpMethod.Get, "/test/api");
        api.Headers.Authorization = new("Bearer", tokens.GetProperty("access_token").GetString());
        (await app.Browser().SendAsync(api, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration", "GET")]
    [InlineData("/.well-known/jwks", "GET")]
    [InlineData("/connect/token", "POST")]
    public async Task The_configured_origin_passes_the_preflight_without_credentials_others_get_no_cors_headers(string path, string method)
    {
        var allowed = await PreflightAsync(path, method, AuthApp.SpaUrl);

        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        allowed.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AuthApp.SpaUrl]);
        allowed.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();

        var refused = await PreflightAsync(path, method, "http://evil.test");
        refused.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task The_discovery_document_names_the_allowed_origin_to_the_browser()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        request.Headers.Add("Origin", AuthApp.SpaUrl);

        var response = await app.Browser().SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AuthApp.SpaUrl]);
    }

    private async Task<HttpResponseMessage> PreflightAsync(string path, string method, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return await app.Browser().SendAsync(request, Ct);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Coworkee.AuthServer.Tests;

public sealed partial class AuthorizationCodeFlowTests(AuthApp app) : IAsyncLifetime
{
    private Coworkee.Contracts.Identity.SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Discovery_document_is_published()
    {
        var discovery = await app.Browser().GetStringAsync("/.well-known/openid-configuration", Ct);

        discovery.ShouldContain("/connect/authorize");
        discovery.ShouldContain("/connect/token");
    }

    [Fact]
    public async Task Anonymous_authorize_redirects_to_login()
    {
        var browser = NoRedirects();
        var response = await browser.GetAsync(AuthorizeUrl(Challenge(Verifier())), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/Account/Login");
    }

    [Fact]
    public async Task Code_flow_issues_tokens_with_user_and_tenant_claims()
    {
        var tokens = await SignInAsync("admin@acme.test", "Admin#12345");

        var access = new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("access_token").GetString());
        access.Claims.Single(c => c.Type == "sub").Value.ShouldBe(_setup.AdminUserId.ToString());
        access.Claims.Single(c => c.Type == "tenant").Value.ShouldBe(_setup.TenantId.ToString());
        access.Audiences.ShouldContain("test_api");
        tokens.TryGetProperty("refresh_token", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Token_storage_is_not_written_to_the_audit_log()
    {
        await SignInAsync("admin@acme.test", "Admin#12345");

        (await app.AuditEntriesForAsync("OpenIddict")).ShouldBe(0);
    }

    [Fact]
    public async Task Refresh_issues_new_tokens()
    {
        var tokens = await SignInAsync("admin@acme.test", "Admin#12345");

        var refreshed = await RefreshAsync(tokens.GetProperty("refresh_token").GetString()!);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deactivated_user_cannot_refresh()
    {
        var tokens = await SignInAsync("admin@acme.test", "Admin#12345");
        await app.DeactivateAsync(_setup.AdminUserId);

        var refreshed = await RefreshAsync(tokens.GetProperty("refresh_token").GetString()!);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refreshed.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_grant");
    }

    [Fact]
    public async Task Deactivated_user_cannot_sign_in()
    {
        await app.DeactivateAsync(_setup.AdminUserId);

        var (response, _) = await PostLoginAsync(NoRedirects(), "admin@acme.test", "Admin#12345", "/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_password_stays_on_login_page()
    {
        var (response, _) = await PostLoginAsync(NoRedirects(), "admin@acme.test", "wrong-password", "/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_redirect_uri_is_rejected()
    {
        var url = AuthorizeUrl(Challenge(Verifier())).Replace(Uri.EscapeDataString(AuthApp.RedirectUri), Uri.EscapeDataString("https://evil.test/cb"), StringComparison.Ordinal);

        var response = await NoRedirects().GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_pkce_is_rejected()
    {
        var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = AuthApp.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = AuthApp.RedirectUri,
            ["scope"] = "openid profile",
            ["state"] = "s",
        });

        var response = await NoRedirects().GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient NoRedirects() => app.Browser();

    private async Task<JsonElement> SignInAsync(string email, string password)
    {
        var browser = NoRedirects();
        var verifier = Verifier();
        var authorize = await browser.GetAsync(AuthorizeUrl(Challenge(verifier)), Ct);
        var loginUrl = authorize.Headers.Location!.ToString();
        var returnUrl = QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), loginUrl).Query)["ReturnUrl"].ToString();

        var (login, _) = await PostLoginAsync(browser, email, password, returnUrl);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var callback = await browser.GetAsync(login.Headers.Location, Ct);
        var code = QueryHelpers.ParseQuery(callback.Headers.Location!.Query)["code"].ToString();
        code.ShouldNotBeNullOrEmpty();

        var token = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = AuthApp.RedirectUri,
            ["client_id"] = AuthApp.ClientId,
            ["code_verifier"] = verifier,
        }), Ct);
        token.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await token.Content.ReadAsStringAsync(Ct)).RootElement;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        NoRedirects().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = AuthApp.ClientId,
        }), Ct);

    private static async Task<(HttpResponseMessage Response, string Html)> PostLoginAsync(HttpClient browser, string email, string password, string returnUrl)
    {
        var page = await browser.GetStringAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), Ct);
        var antiforgery = AntiforgeryToken().Match(page).Groups[1].Value;
        var response = await browser.PostAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = antiforgery,
        }), Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    private static string AuthorizeUrl(string challenge) => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
    {
        ["client_id"] = AuthApp.ClientId,
        ["response_type"] = "code",
        ["redirect_uri"] = AuthApp.RedirectUri,
        ["scope"] = "openid profile email roles offline_access test_api",
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
        ["state"] = "state",
    });

    private static string Verifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coworkee.Application.Messaging;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

/// <summary>A browser walking through sign-in and the authorization code flow, for the session and client tests.</summary>
internal sealed partial class OidcFlow(AuthApp app, string clientId = AuthApp.ClientId, string redirectUri = AuthApp.RedirectUri, string? clientSecret = null)
{
    private readonly string _verifier = Base64Url(RandomNumberGenerator.GetBytes(32));

    public HttpClient Browser { get; } = app.Browser();

    public string Scope { get; init; } = "openid profile email roles offline_access test_api";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public string AuthorizeUrl => QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
    {
        ["client_id"] = clientId,
        ["response_type"] = "code",
        ["redirect_uri"] = redirectUri,
        ["scope"] = Scope,
        ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(_verifier))),
        ["code_challenge_method"] = "S256",
        ["state"] = "state",
    });

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, string returnUrl = "/")
    {
        var (response, _) = await PostFormAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), new() { ["Input.Email"] = email, ["Input.Password"] = password });
        return response;
    }

    public Task<HttpResponseMessage> AuthorizeAsync() => Browser.GetAsync(AuthorizeUrl, Ct);

    /// <summary>Follows local redirects until the browser leaves for the client (the code) or stops at a page.</summary>
    public async Task<HttpResponseMessage> FollowAsync(HttpResponseMessage response)
    {
        while (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location is { } location && !location.IsAbsoluteUri)
        {
            response = await Browser.GetAsync(location, Ct);
        }

        return response;
    }

    public async Task<JsonElement> SignInAsync(string email, string password)
    {
        (await LoginAsync(email, password, AuthorizeUrl)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return await RedeemAsync(await FollowAsync(await AuthorizeAsync()));
    }

    public async Task<JsonElement> RedeemAsync(HttpResponseMessage callback)
    {
        callback.Headers.Location!.ToString().ShouldStartWith(redirectUri);
        var code = QueryHelpers.ParseQuery(callback.Headers.Location.Query)["code"].ToString();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = _verifier,
        };
        if (clientSecret is not null)
        {
            form["client_secret"] = clientSecret;
        }

        var token = await Browser.PostAsync("/connect/token", new FormUrlEncodedContent(form), Ct);
        token.StatusCode.ShouldBe(HttpStatusCode.OK, await token.Content.ReadAsStringAsync(Ct));
        return JsonDocument.Parse(await token.Content.ReadAsStringAsync(Ct)).RootElement;
    }

    public Task<HttpResponseMessage> RefreshAsync(JsonElement tokens) =>
        Browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.GetProperty("refresh_token").GetString()!,
            ["client_id"] = clientId,
        }), Ct);

    public async Task<(HttpResponseMessage Response, string Html)> PostFormAsync(string url, Dictionary<string, string> fields, string? handler = null)
    {
        var page = await Browser.GetStringAsync(url, Ct);
        fields["__RequestVerificationToken"] = Antiforgery().Match(page).Groups[1].Value;
        var target = handler is null ? url : QueryHelpers.AddQueryString(url, "handler", handler);
        var response = await Browser.PostAsync(target, new FormUrlEncodedContent(fields), Ct);
        return (response, await response.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>Sends a command as the given user, the way the API does for a signed-in administrator.</summary>
    public static async Task<T> SendAsync<T>(AuthApp app, Guid userId, Guid tenantId, IRequest<T> request)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, Ct);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();
}

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Coworkee.AuthServer.Clients;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class ClientAdminTests(AuthApp app) : IAsyncLifetime
{
    private const string Admin = "admin@acme.test";
    private const string Password = "Admin#12345";
    private const string Callback = "https://other.test/cb";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Clients_and_scopes_from_the_configuration_are_read_only()
    {
        var configured = (await SendAsync(new GetClients())).Value.Single(c => c.ClientId == AuthApp.ClientId);
        var scope = (await SendAsync(new GetScopes())).Value.Single(s => s.Name == "test_api");

        configured.Managed.ShouldBeTrue();
        scope.BuiltIn.ShouldBeTrue();
        scope.Resources.ShouldBe(["test_api"]);
        (await SendAsync(new DeleteClient(configured.Id))).Error!.Code.ShouldBe("auth.client_managed");
        (await SendAsync(new DeleteScope(scope.Id))).Error!.Code.ShouldBe("auth.scope_managed");
    }

    [Fact]
    public async Task A_confidential_client_gets_its_secret_once_and_signs_in_with_it()
    {
        var created = (await SendAsync(new CreateClient(Request("partner", "confidential", "implicit")))).Value;
        created.ClientSecret.ShouldNotBeNullOrEmpty();

        var tokens = await new OidcFlow(app, "partner", Callback, created.ClientSecret) { Scope = "openid profile offline_access" }.SignInAsync(Admin, Password);

        tokens.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
        (await SendAsync(new GetClients())).Value.Single(c => c.ClientId == "partner").Managed.ShouldBeFalse();
        (await SendAsync(new CreateClient(Request("partner", "public", "implicit")))).Error!.Code.ShouldBe("auth.client_exists");
    }

    [Fact]
    public async Task Invalid_clients_are_refused()
    {
        var result = await SendAsync(new CreateClient(Request("bad id", "secret-ish", "implicit") with { RedirectUris = ["not-an-address"] }));

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Explicit_consent_asks_once_and_the_user_can_revoke_the_application()
    {
        await SendAsync(new CreateClient(Request("partner", "public", "explicit")));
        var flow = new OidcFlow(app, "partner", Callback) { Scope = "openid profile offline_access" };
        await flow.LoginAsync(Admin, Password, flow.AuthorizeUrl);

        var consent = await flow.FollowAsync(await flow.AuthorizeAsync());
        var page = consent.RequestMessage!.RequestUri!.PathAndQuery;
        page.ShouldStartWith("/Account/Consent");
        (await consent.Content.ReadAsStringAsync(Ct)).ShouldContain("asks for access");
        var (allowed, _) = await flow.PostFormAsync(page, [], "Allow");
        var tokens = await flow.RedeemAsync(await flow.FollowAsync(allowed));
        (await flow.FollowAsync(await flow.AuthorizeAsync())).Headers.Location!.ToString().ShouldStartWith(Callback);

        var applications = await flow.Browser.GetStringAsync("/Account/Manage/Applications", Ct);
        applications.ShouldContain("Partner");
        var id = System.Text.RegularExpressions.Regex.Match(applications, "name=\"applicationId\" value=\"([^\"]+)\"").Groups[1].Value;
        (await flow.PostFormAsync("/Account/Manage/Applications", new() { ["applicationId"] = id })).Html.ShouldContain("The access was revoked");

        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await flow.FollowAsync(await flow.AuthorizeAsync())).RequestMessage!.RequestUri!.PathAndQuery.ShouldStartWith("/Account/Consent");
    }

    [Fact]
    public async Task Declining_the_consent_returns_an_error_to_the_client()
    {
        await SendAsync(new CreateClient(Request("partner", "public", "explicit")));
        var flow = new OidcFlow(app, "partner", Callback) { Scope = "openid profile" };
        await flow.LoginAsync(Admin, Password, flow.AuthorizeUrl);
        var page = (await flow.FollowAsync(await flow.AuthorizeAsync())).RequestMessage!.RequestUri!.PathAndQuery;

        var (denied, _) = await flow.PostFormAsync(page, [], "Deny");

        var callback = await flow.FollowAsync(denied);
        callback.Headers.Location!.ToString().ShouldStartWith(Callback);
        callback.Headers.Location.Query.ShouldContain("error=consent_required");
    }

    [Fact]
    public async Task Stored_scopes_add_their_resources_to_the_access_token()
    {
        (await SendAsync(new CreateScope(new ScopeRequest("reports", "Reports", null, ["reports_api"])))).IsSuccess.ShouldBeTrue();
        await SendAsync(new CreateClient(Request("partner", "public", "implicit") with { Scopes = ["openid", "reports"] }));

        var tokens = await new OidcFlow(app, "partner", Callback) { Scope = "openid reports" }.SignInAsync(Admin, Password);

        new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("access_token").GetString()).Audiences.ShouldContain("reports_api");
    }

    [Fact]
    public async Task Account_pages_allow_the_origins_of_stored_clients_and_show_the_branding()
    {
        await SendAsync(new CreateClient(Request("partner", "public", "implicit")));
        await app.App.Services.GetRequiredService<HybridCache>().RemoveAsync("coworkee:auth-client-uris", Ct);

        using var response = await app.Browser().GetAsync("/Account/Login", Ct);

        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("https://other.test");
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("data-testid=\"brand\"");
        html.ShouldContain("--signal:");
    }

    private static ClientRequest Request(string clientId, string type, string consent) =>
        new(clientId, "Partner", type, consent, [Callback], [], [ClientGrantTypes.AuthorizationCode, ClientGrantTypes.RefreshToken], ["openid", "profile", "offline_access"]);

    private Task<T> SendAsync<T>(Coworkee.Application.Messaging.IRequest<T> request) => OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, request);
}

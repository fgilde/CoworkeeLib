using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Users;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Tests;

public sealed class SessionTests(AuthApp app) : IAsyncLifetime
{
    private const string Password = "Passw0rd!x";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Access_tokens_carry_a_hash_of_the_security_stamp()
    {
        var tokens = await new OidcFlow(app).SignInAsync("admin@acme.test", "Admin#12345");

        var access = new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("access_token").GetString());
        access.Claims.Single(c => c.Type == SessionStamp.ClaimType).Value.Length.ShouldBe(16);
        new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("id_token").GetString()).Claims.ShouldNotContain(c => c.Type == SessionStamp.ClaimType);
    }

    [Fact]
    public async Task Signing_out_everywhere_revokes_the_tokens_the_api_access_and_the_auth_server_sign_in()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        var tokens = await flow.SignInAsync("bob@acme.test", Password);
        (await CallApiAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await AsAdminAsync(new SignOutUser(bob))).IsSuccess.ShouldBeTrue();

        (await CallApiAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var refreshed = await flow.RefreshAsync(tokens);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refreshed.Content.ReadAsStringAsync(Ct)).ShouldContain("invalid_grant");
        (await ValidTokensAsync(bob)).ShouldBe(0);
        (await flow.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
    }

    [Fact]
    public async Task A_locked_user_is_signed_out_and_cannot_sign_in_until_unlocked()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var flow = new OidcFlow(app);
        var tokens = await flow.SignInAsync("bob@acme.test", Password);

        (await AsAdminAsync(new LockUser(bob, null))).IsSuccess.ShouldBeTrue();

        (await flow.RefreshAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await new OidcFlow(app).LoginAsync("bob@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AsAdminAsync(new UnlockUser(bob))).IsSuccess.ShouldBeTrue();
        (await new OidcFlow(app).LoginAsync("bob@acme.test", Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Locks_end_in_the_future_and_administrators_keep_their_own_session()
    {
        var bob = await CreateUserAsync("bob@acme.test");

        (await AsAdminAsync(new LockUser(bob, DateTimeOffset.UtcNow.AddMinutes(-1)))).Error!.Kind.ShouldBe(Coworkee.Core.Results.ErrorKind.Validation);
        (await AsAdminAsync(new SignOutUser(_setup.AdminUserId))).Error!.Code.ShouldBe("identity.own_session");
    }

    private Task<Coworkee.Core.Results.Result> AsAdminAsync(Coworkee.Application.Messaging.IRequest<Coworkee.Core.Results.Result> request) =>
        OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, request);

    private async Task<Guid> CreateUserAsync(string email) =>
        (await OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, new CreateUser(new CreateUserRequest(email, Password, null, null)))).Value.Id;

    private async Task<HttpResponseMessage> CallApiAsync(JsonElement tokens)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/api");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        return await app.Browser().SendAsync(request, Ct);
    }

    private async Task<int> ValidTokensAsync(Guid userId)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var valid = 0;
        await foreach (var token in tokens.FindBySubjectAsync(userId.ToString(), Ct))
        {
            valid += await tokens.HasStatusAsync(token, Statuses.Valid, Ct) ? 1 : 0;
        }

        return valid;
    }
}

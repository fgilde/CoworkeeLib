using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Sessions;
using Coworkee.Identity.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Tests;

public sealed class UserSessionTests(AuthApp app) : IAsyncLifetime
{
    private const string Password = "Passw0rd!x";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sign_in_beyond_a_limit_of_one_ends_the_older_session_and_revokes_its_tokens()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        await app.SetSettingAsync(AuthSettings.MaxSessionsPerUser, "1");
        var first = new OidcFlow(app);
        var firstTokens = await first.SignInAsync("bob@acme.test", Password);
        var second = new OidcFlow(app);
        var secondTokens = await second.SignInAsync("bob@acme.test", Password);

        (await CallApiAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await first.RefreshAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await first.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
        (await ValidAuthorizationsAsync(bob, Sid(firstTokens))).ShouldBe(0);
        (await SessionAsync(Sid(firstTokens))).EndReason.ShouldBe(SessionEndReasons.Limit);

        (await CallApiAsync(secondTokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.RefreshAsync(secondTokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await SessionAsync(Sid(secondTokens));
        session.EndedAt.ShouldBeNull();
        session.ClientId.ShouldBe(AuthApp.ClientId);
    }

    [Fact]
    public async Task An_administrator_ends_one_session_and_the_others_go_on()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var first = new OidcFlow(app);
        var firstTokens = await first.SignInAsync("bob@acme.test", Password);
        var second = new OidcFlow(app);
        var secondTokens = await second.SignInAsync("bob@acme.test", Password);
        var listed = (await AsAdminAsync(new GetUserSessions(bob))).Value;
        listed.Sessions.Count.ShouldBe(2);
        (await AsAdminAsync(new GetSessionCounts([bob]))).Value[bob].ShouldBe(2);

        (await AsAdminAsync(new EndSession(Guid.Parse(Sid(firstTokens))))).IsSuccess.ShouldBeTrue();

        (await CallApiAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await first.RefreshAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await first.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
        (await ValidAuthorizationsAsync(bob, Sid(firstTokens))).ShouldBe(0);
        var ended = await SessionAsync(Sid(firstTokens));
        (ended.EndReason, ended.EndedBy).ShouldBe((SessionEndReasons.Ended, _setup.AdminUserId));
        (await CallApiAsync(secondTokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.RefreshAsync(secondTokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ValidAuthorizationsAsync(bob, Sid(secondTokens))).ShouldBe(1);
        (await AsAdminAsync(new GetUserSessions(bob))).Value.Sessions.Select(s => s.Id).ShouldBe([Guid.Parse(Sid(secondTokens))]);
    }

    [Fact]
    public async Task A_users_own_limit_wins_over_the_organisations()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var eve = await CreateUserAsync("eve@acme.test");
        await app.SetSettingAsync(AuthSettings.MaxSessionsPerUser, "1");
        (await AsAdminAsync(new SetSessionLimit(bob, 0))).IsSuccess.ShouldBeTrue();
        (await AsAdminAsync(new SetSessionLimit(eve, 2))).IsSuccess.ShouldBeTrue();

        var bobs = new List<JsonElement>();
        var eves = new List<JsonElement>();
        for (var i = 0; i < 3; i++)
        {
            bobs.Add(await new OidcFlow(app).SignInAsync("bob@acme.test", Password));
            eves.Add(await new OidcFlow(app).SignInAsync("eve@acme.test", Password));
        }

        foreach (var tokens in bobs)
        {
            (await CallApiAsync(tokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await CallApiAsync(eves[0])).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CallApiAsync(eves[1])).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CallApiAsync(eves[2])).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AsAdminAsync(new SetSessionLimit(bob, -1))).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task In_the_refuse_mode_a_sign_in_beyond_the_limit_is_refused_and_the_older_session_goes_on()
    {
        await CreateUserAsync("bob@acme.test");
        await app.SetSettingAsync(AuthSettings.MaxSessionsPerUser, "1");
        await app.SetSettingAsync(AuthSettings.SessionLimitMode, SessionLimitModes.Refuse);
        var first = new OidcFlow(app);
        var firstTokens = await first.SignInAsync("bob@acme.test", Password);

        var second = new OidcFlow(app);
        var (response, html) = await second.PostFormAsync("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(second.AuthorizeUrl),
            new() { ["Input.Email"] = "bob@acme.test", ["Input.Password"] = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("You are signed in on too many devices.");
        (await second.AuthorizeAsync()).Headers.Location!.ToString().ShouldContain("/Account/Login");
        (await CallApiAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.RefreshAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await app.InDbAsync(db => db.Set<UserSession>().IgnoreQueryFilters().CountAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Users_end_their_other_sessions_and_signing_out_ends_the_own_one()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var first = new OidcFlow(app);
        var firstTokens = await first.SignInAsync("bob@acme.test", Password);
        var second = new OidcFlow(app);
        var secondTokens = await second.SignInAsync("bob@acme.test", Password);

        (await AsAsync(bob, new GetMySessions())).Value.Count.ShouldBe(2);
        (await AsAsync(bob, new EndSession(Guid.Parse(Sid(firstTokens))))).IsSuccess.ShouldBeTrue();
        (await SessionAsync(Sid(firstTokens))).EndReason.ShouldBe(SessionEndReasons.EndedByUser);
        (await CallApiAsync(firstTokens)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await second.Browser.GetAsync("/connect/endsession", Ct)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await SessionAsync(Sid(secondTokens))).EndReason.ShouldBe(SessionEndReasons.LoggedOut);
        (await second.RefreshAsync(secondTokens)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AsAsync(bob, new GetMySessions())).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_the_rows_of_every_session()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        await new OidcFlow(app).SignInAsync("bob@acme.test", Password);
        await new OidcFlow(app).SignInAsync("bob@acme.test", Password);

        (await AsAdminAsync(new EndUserSessions(bob))).IsSuccess.ShouldBeTrue();

        (await AsAdminAsync(new GetUserSessions(bob))).Value.Sessions.ShouldBeEmpty();
        (await app.InDbAsync(db => db.Set<UserSession>().IgnoreQueryFilters().Select(s => s.EndReason).ToListAsync(Ct))).ShouldAllBe(r => r == "signed-out");
    }

    [Fact]
    public async Task Sessions_of_another_organisation_stay_out_of_reach()
    {
        var (foreignUser, foreignSession) = await app.InDbAsync(async db =>
        {
            var tenant = new Tenant { Name = "Other", Identifier = "other" };
            var user = new User { UserName = "olga@other.test", Email = "olga@other.test", TenantId = tenant.Id };
            var session = new UserSession(Guid.NewGuid())
            {
                TenantId = tenant.Id, UserId = user.Id, CreatedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            };
            db.AddRange(tenant, user, session);
            await db.SaveChangesAsync(Ct);
            return (user.Id, session.Id);
        });

        (await AsAdminAsync(new EndSession(foreignSession))).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await AsAdminAsync(new GetUserSessions(foreignUser))).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await AsAdminAsync(new SetSessionLimit(foreignUser, 1))).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await AsAdminAsync(new GetSessionCounts([foreignUser]))).Value.ShouldBeEmpty();
        (await SessionAsync(foreignSession.ToString("N"))).EndedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Without_the_session_permissions_the_requests_are_refused()
    {
        var bob = await CreateUserAsync("bob@acme.test");
        var admin = await new OidcFlow(app).SignInAsync("admin@acme.test", "Admin#12345");

        (await AsAsync(bob, new GetUserSessions(_setup.AdminUserId))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await AsAsync(bob, new GetSessionCounts([_setup.AdminUserId]))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await AsAsync(bob, new EndUserSessions(_setup.AdminUserId))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await AsAsync(bob, new SetSessionLimit(_setup.AdminUserId, 1))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        // someone else's session is unknown without the permission
        (await AsAsync(bob, new EndSession(Guid.Parse(Sid(admin))))).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await CallApiAsync(admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static string Sid(JsonElement tokens) =>
        new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("access_token").GetString()).Claims.Single(c => c.Type == SessionStamp.SessionClaimType).Value;

    private Task<T> AsAdminAsync<T>(IRequest<T> request) => AsAsync(_setup.AdminUserId, request);

    private Task<T> AsAsync<T>(Guid userId, IRequest<T> request) => OidcFlow.SendAsync(app, userId, _setup.TenantId, request);

    private async Task<Guid> CreateUserAsync(string email) =>
        (await AsAdminAsync(new CreateUser(new CreateUserRequest(email, Password, null, null)))).Value.Id;

    private Task<UserSession> SessionAsync(string sid) =>
        app.InDbAsync(db => db.Set<UserSession>().IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == Guid.Parse(sid), Ct));

    private async Task<HttpResponseMessage> CallApiAsync(JsonElement tokens)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/api");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        return await app.Browser().SendAsync(request, Ct);
    }

    private async Task<int> ValidAuthorizationsAsync(Guid userId, string sid)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var valid = 0;
        await foreach (var authorization in authorizations.FindBySubjectAsync(userId.ToString(), Ct))
        {
            valid += AuthSessions.Names(await authorizations.GetPropertiesAsync(authorization, Ct), AuthSessions.Property, sid)
                     && await authorizations.HasStatusAsync(authorization, Statuses.Valid, Ct) ? 1 : 0;
        }

        return valid;
    }
}

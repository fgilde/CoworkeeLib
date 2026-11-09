using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SessionPagesTests : ClientTestBase
{
    private static readonly Guid AdaId = Guid.CreateVersion7();
    private readonly ISessionsApi _sessions = Substitute.For<ISessionsApi>();
    private readonly FakeODataClient _odata = new();

    public SessionPagesTests()
    {
        Services.AddSingleton(_sessions);
        Services.AddSingleton<IODataClient>(_odata);
        AddAuthorization().SetAuthorized("Ada").SetClaims(new System.Security.Claims.Claim("sub", AdaId.ToString()), new System.Security.Claims.Claim("sid", "mine"))
            .SetPolicies(PermissionPolicy.For(IdentityPermissions.Sessions.View), PermissionPolicy.For(IdentityPermissions.Sessions.Manage));
        Render<MudPopoverProvider>();
    }

    [Fact]
    public async Task My_sessions_end_the_others_but_not_this_one_and_follow_new_sign_ins()
    {
        var mine = Session("Windows") with { Current = true };
        var phone = Session("iPhone");
        _sessions.GetMineAsync(default).ReturnsForAnyArgs([mine, phone]);
        var dialogs = Render<MudDialogProvider>();
        var page = Render<ProfileSessions>();
        page.WaitForAssertion(() => page.FindAll("[data-session]").Count.ShouldBe(2));

        page.Find($"[data-session='{mine.Id}'] [data-testid='session-current']").ShouldNotBeNull();
        page.FindAll($"[data-session='{mine.Id}'] [data-testid='session-end']").ShouldBeEmpty();
        _sessions.GetMineAsync(default).ReturnsForAnyArgs([mine]);
        var ending = await ClickUntilConfirmShowsAsync(page, $"[data-session='{phone.Id}'] [data-testid='session-end']", dialogs, "iPhone");
        Task confirming = Task.CompletedTask;
        await dialogs.InvokeAsync(() => { confirming = dialogs.FindAll("button").First(b => b.TextContent.Trim() == "End").ClickAsync(new()); });
        await confirming;
        await ending;
        await _sessions.Received(1).EndAsync(phone.Id, Arg.Any<CancellationToken>());
        page.WaitForAssertion(() => page.FindAll("[data-session]").Count.ShouldBe(1));

        // a sign-in elsewhere shows up without a reload
        _sessions.GetMineAsync(default).ReturnsForAnyArgs([mine, Session("Android")]);
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(AdaId), RealtimeEventTypes.EntityChanged,
            new EntityChangedPayload("UserSession", Guid.NewGuid().ToString(), EntityChangedPayload.Created, []));
        page.WaitForAssertion(() => page.Markup.ShouldContain("Android"));
    }

    [Fact]
    public async Task Administrators_see_the_sessions_with_their_users_and_counts_and_end_one()
    {
        var bob = Guid.CreateVersion7();
        var session = Session("Linux") with { UserId = bob, ImpersonatorId = AdaId };
        _odata.With("UserSessions", session);
        Api.GetUserNamesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, string> { [AdaId] = "Ada Lovelace", [bob] = "Bob Builder" });
        _sessions.GetCountsAsync(default!, default).ReturnsForAnyArgs(new Dictionary<Guid, int> { [bob] = 3 });
        var dialogs = Render<MudDialogProvider>();

        var page = Render<Pages.Admin.Sessions>();

        // generous waits: under the full suite's load the page and the dialog render later than bUnit's default second
        page.WaitForAssertion(() => page.Markup.ShouldContain("Bob Builder"), TimeSpan.FromSeconds(10));
        page.Find($"[data-testid='session-count-{session.Id}']").TextContent.Trim().ShouldBe("3");
        page.Find("[data-testid='session-impersonated']").TextContent.ShouldContain("Ada Lovelace");
        var ending = await ClickUntilConfirmShowsAsync(page, $"[data-testid='session-end-{session.Id}']", dialogs, "Bob Builder");
        Task confirming = Task.CompletedTask;
        await dialogs.InvokeAsync(() => { confirming = dialogs.FindAll("button").First(b => b.TextContent.Trim() == "End").ClickAsync(new()); });
        await confirming;
        await ending;
        await _sessions.Received(1).EndAsync(session.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_user_page_shows_his_own_limit_or_the_organisations()
    {
        _sessions.GetOfUserAsync(default).ReturnsForAnyArgs(new UserSessionsDto(null, [Session("Windows")]));
        var part = Render<Pages.Admin.UserSessions>(p => p.Add(c => c.UserId, Guid.CreateVersion7()).Add(c => c.UserName, "Bob"));
        part.WaitForAssertion(() => part.Find("[data-testid='session-limit-text']").TextContent.ShouldContain("unlimited (organisation setting)"));

        _sessions.GetOfUserAsync(default).ReturnsForAnyArgs(new UserSessionsDto(1, []));
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.Type("UserSession"));
        part.WaitForAssertion(() => part.Find("[data-testid='session-limit-text']").TextContent.Trim().ShouldBe("Session limit: 1"));
    }

    [Fact]
    public async Task The_guard_ignores_the_end_of_another_session_and_tells_why_its_own_ended()
    {
        Api.LogoutAsync(Arg.Any<CancellationToken>()).Returns(new BffLogoutDto("/"));
        var dialogs = Render<MudDialogProvider>();
        Render<SessionGuard>();
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();

        realtime.Push(RealtimeTopics.User(AdaId), RealtimeEventTypes.SessionRevoked, new SessionRevokedPayload("ended", EndedSessionId: "other"));
        await Api.DidNotReceive().LogoutAsync(Arg.Any<CancellationToken>());

        realtime.Push(RealtimeTopics.User(AdaId), RealtimeEventTypes.SessionRevoked, new SessionRevokedPayload("limit", EndedSessionId: "mine"));
        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("only so many sessions are allowed"));
        await Api.Received(1).LogoutAsync(Arg.Any<CancellationToken>());
    }

    private static UserSessionDto Session(string device) =>
        new(Guid.NewGuid(), AdaId, "web", device, "Chrome 130", "10.0.0.1", null, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14), null, null);

    // found and clicked in one go on the renderer (live lists re-render meanwhile); ponytail: under bUnit MudEx's confirmation now and then returns at
    // once or never reaches the provider, so the click is repeated (at most three times) until the dialog is there; the tests still check that exactly
    // one confirmation went through
    private static async Task<Task> ClickUntilConfirmShowsAsync<T>(IRenderedComponent<T> page, string selector, IRenderedComponent<MudDialogProvider> dialogs, string text)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        Task clicking = Task.CompletedTask;
        for (var attempt = 0; ; attempt++)
        {
            await page.InvokeAsync(() => { clicking = page.Find(selector).ClickAsync(new()); });
            try
            {
                dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain(text), TimeSpan.FromSeconds(attempt < 2 ? 3 : 10));
                return clicking;
            }
            catch (Bunit.Extensions.WaitForHelpers.WaitForFailedException) when (attempt < 2)
            {
            }
        }
    }
}

using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SessionGuardTests : ClientTestBase
{
    private readonly Guid _me = Guid.CreateVersion7();

    public SessionGuardTests()
    {
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", _me.ToString()));
        Api.LogoutAsync(Arg.Any<CancellationToken>()).Returns(new BffLogoutDto("https://auth.test/connect/endsession"));
    }

    [Fact]
    public async Task A_revoked_session_signs_out_tells_why_and_leaves()
    {
        var dialogs = Render<MudBlazor.MudDialogProvider>();
        Render<SessionGuard>();
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();

        realtime.Push(RealtimeTopics.User(_me), RealtimeEventTypes.SessionRevoked, new SessionRevokedPayload("locked"));

        dialogs.WaitForAssertion(() => dialogs.Markup.ShouldContain("An administrator locked your account."));
        await Api.Received(1).LogoutAsync(Arg.Any<CancellationToken>());
        await dialogs.FindAll("button").Last().ClickAsync(new());
        dialogs.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("https://auth.test/connect/endsession"));
    }

    [Fact]
    public async Task Other_events_on_the_user_topic_keep_the_session()
    {
        Render<SessionGuard>();

        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(_me));

        await Api.DidNotReceive().LogoutAsync(Arg.Any<CancellationToken>());
    }
}

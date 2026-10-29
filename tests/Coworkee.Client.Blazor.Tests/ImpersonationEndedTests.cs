using System.Security.Claims;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ImpersonationEndedTests : ClientTestBase
{
    private readonly Guid _target = Guid.CreateVersion7();

    private readonly Guid _admin = Guid.CreateVersion7();

    [Fact]
    public void The_impersonating_session_follows_its_own_topic_and_signs_in_again_when_it_ends()
    {
        AddAuthorization().SetAuthorized("Bob").SetClaims(new Claim("sub", _target.ToString()), new Claim("sid", "helper"), new Claim(ImpersonationClaims.Impersonator, _admin.ToString()));
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("orders/7");
        Render<SessionGuard>();
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();

        realtime.Invocations.ShouldContain((RealtimeHubMethods.Subscribe, RealtimeTopics.Session("helper")));
        realtime.Push(RealtimeTopics.Session("helper"), RealtimeEventTypes.ImpersonationEnded, new ImpersonationEndedPayload(_target, _admin));

        nav.Uri.ShouldEndWith("/bff/login?returnUrl=%2Forders%2F7");
    }

    [Fact]
    public void The_users_own_session_follows_no_session_topic_and_goes_on()
    {
        AddAuthorization().SetAuthorized("Bob").SetClaims(new Claim("sub", _target.ToString()), new Claim("sid", "own"));
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("orders/7");
        Render<SessionGuard>();
        var realtime = Services.GetRequiredService<FakeRealtimeConnection>();

        realtime.Invocations.ShouldNotContain(i => i.Topic.StartsWith(RealtimeTopics.SessionPrefix));
        realtime.Push(RealtimeTopics.User(_target), RealtimeEventTypes.ImpersonationEnded, new ImpersonationEndedPayload(_target, _admin));

        nav.Uri.ShouldEndWith("/orders/7");
    }
}

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
    public void A_session_signed_in_as_the_user_by_that_impersonator_signs_in_again()
    {
        AddAuthorization().SetAuthorized("Bob").SetClaims(new Claim("sub", _target.ToString()), new Claim(ImpersonationClaims.Impersonator, _admin.ToString()));
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("orders/7");
        Render<SessionGuard>();

        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(_target), RealtimeEventTypes.ImpersonationEnded, new ImpersonationEndedPayload(_target, _admin));

        nav.Uri.ShouldEndWith("/bff/login?returnUrl=%2Forders%2F7");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_users_own_session_and_other_impersonations_go_on(bool impersonatedByAnother)
    {
        AddAuthorization().SetAuthorized("Bob").SetClaims(
            [new Claim("sub", _target.ToString()), .. impersonatedByAnother ? [new Claim(ImpersonationClaims.Impersonator, Guid.CreateVersion7().ToString())] : Array.Empty<Claim>()]);
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("orders/7");
        Render<SessionGuard>();

        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(_target), RealtimeEventTypes.ImpersonationEnded, new ImpersonationEndedPayload(_target, _admin));

        nav.Uri.ShouldEndWith("/orders/7");
    }
}

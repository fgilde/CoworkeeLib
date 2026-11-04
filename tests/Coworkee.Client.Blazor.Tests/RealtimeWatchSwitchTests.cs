using Bunit;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The switch of a watched set (AddRealtimeWatch): shown to who may manage watches, off and on again with one click.</summary>
public sealed class RealtimeWatchSwitchTests : ClientTestBase
{
    private readonly IRealtimeWatchApi _watches = Substitute.For<IRealtimeWatchApi>();

    private const string Input = "input[data-testid='realtime-watch'], [data-testid='realtime-watch'] input";

    public RealtimeWatchSwitchTests() => Services.AddSingleton(_watches);

    [Fact]
    public void Who_may_manage_watches_switches_the_watch_of_the_set()
    {
        _watches.GetAsync(default).ReturnsForAnyArgs([new RealtimeWatchDto("Parcel", true, TimeSpan.FromSeconds(30), true)]);
        var watch = Render<RealtimeWatchSwitch>(p => p.Add(s => s.EntityType, "Parcel"));

        watch.WaitForAssertion(() => watch.Find(Input).HasAttribute("checked").ShouldBeTrue());
        watch.Find(Input).Change(false);

        watch.WaitForAssertion(() => _watches.Received().SetAsync("Parcel", false, Arg.Any<CancellationToken>()));
        watch.Find(Input).HasAttribute("checked").ShouldBeFalse();
    }

    [Fact]
    public void Who_may_not_manage_watches_and_sets_without_a_watch_show_no_switch()
    {
        _watches.GetAsync(default).ReturnsForAnyArgs([new RealtimeWatchDto("Parcel", true, TimeSpan.FromSeconds(30), false)]);
        var readOnly = Render<RealtimeWatchSwitch>(p => p.Add(s => s.EntityType, "Parcel"));
        var unwatched = Render<RealtimeWatchSwitch>(p => p.Add(s => s.EntityType, "Other"));

        readOnly.WaitForAssertion(() => _watches.ReceivedWithAnyArgs().GetAsync(default));
        readOnly.FindAll("[data-testid='realtime-watch']").ShouldBeEmpty();
        unwatched.FindAll("[data-testid='realtime-watch']").ShouldBeEmpty();
    }
}

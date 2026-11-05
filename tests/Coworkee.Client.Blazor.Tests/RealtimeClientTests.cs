using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class RealtimeClientTests : ClientTestBase
{
    [Fact]
    public async Task Resubscribes_every_topic_after_a_reconnect()
    {
        var connection = new FakeRealtimeConnection();
        var client = new RealtimeClient(connection);
        await client.SubscribeAsync("type:User", _ => Task.CompletedTask);
        await client.SubscribeAsync("type:Role", _ => Task.CompletedTask);

        await connection.ReconnectAsync();

        connection.Invocations.Count(i => i == (RealtimeHubMethods.Subscribe, "type:User")).ShouldBe(2);
        connection.Invocations.Count(i => i == (RealtimeHubMethods.Subscribe, "type:Role")).ShouldBe(2);
    }

    [Fact]
    public async Task A_subscription_made_while_the_hub_is_down_gets_its_events_once_the_hub_is_back()
    {
        // e.g. the settings of the layout: subscribed once at the start, never again; without a retry the tab missed every change until a reload
        var connection = new FakeRealtimeConnection { FailStarts = 1 };
        var client = new RealtimeClient(connection);
        var received = 0;

        var subscription = await client.SubscribeAsync("settings:global", _ => { received++; return Task.CompletedTask; });

        await WaitUntilAsync(() => connection.Invocations.Contains((RealtimeHubMethods.Subscribe, "settings:global")));
        connection.Push("settings:global");
        received.ShouldBe(1);
        RealtimeClient.IsActive(subscription).ShouldBeTrue();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20, Xunit.TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue();
    }

    [Fact]
    public void Retry_policy_never_gives_up()
    {
        var policy = new ForeverRetryPolicy();

        policy.NextRetryDelay(new Microsoft.AspNetCore.SignalR.Client.RetryContext { PreviousRetryCount = 0 }).ShouldBe(TimeSpan.Zero);
        policy.NextRetryDelay(new Microsoft.AspNetCore.SignalR.Client.RetryContext { PreviousRetryCount = 500 }).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Events_reach_only_handlers_of_their_topic_and_last_dispose_unsubscribes()
    {
        var connection = new FakeRealtimeConnection();
        var client = new RealtimeClient(connection);
        var users = 0;
        var roles = 0;
        var first = await client.SubscribeAsync("type:User", _ => { users++; return Task.CompletedTask; });
        var second = await client.SubscribeAsync("type:User", _ => { users++; return Task.CompletedTask; });
        await client.SubscribeAsync("type:Role", _ => { roles++; return Task.CompletedTask; });

        connection.Push("type:User");
        await first.DisposeAsync();

        users.ShouldBe(2);
        roles.ShouldBe(0);
        connection.Invocations.ShouldNotContain((RealtimeHubMethods.Unsubscribe, "type:User"));
        connection.Invocations.Count(i => i == (RealtimeHubMethods.Subscribe, "type:User")).ShouldBe(1);

        await second.DisposeAsync();

        connection.Invocations.ShouldContain((RealtimeHubMethods.Unsubscribe, "type:User"));
    }

    [Fact]
    public async Task Subscription_component_unsubscribes_when_disposed()
    {
        var connection = Services.GetRequiredService<FakeRealtimeConnection>();
        var component = Render<RealtimeSubscription>(p => p.Add(s => s.Topic, "type:Theme").Add(s => s.OnEvent, (RealtimeEnvelope _) => { }));
        component.WaitForAssertion(() => connection.Invocations.ShouldContain((RealtimeHubMethods.Subscribe, "type:Theme")));

        await DisposeComponentsAsync();

        component.WaitForAssertion(() => connection.Invocations.ShouldContain((RealtimeHubMethods.Unsubscribe, "type:Theme")));
    }

    [Fact]
    public void Bell_shows_the_unread_count_and_updates_on_push()
    {
        var userId = Guid.CreateVersion7();
        Api.GetUnreadNotificationCountAsync(Arg.Any<CancellationToken>()).Returns(new UnreadCountDto(2), new UnreadCountDto(3));
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", userId.ToString()));
        var connection = Services.GetRequiredService<FakeRealtimeConnection>();

        var bell = Render<NotificationBell>();
        bell.WaitForAssertion(() => bell.Find("[data-testid='unread']").TextContent.ShouldContain("2"));

        connection.Push(RealtimeTopics.User(userId), RealtimeEventTypes.EntityChanged, new EntityChangedPayload("Notification", "1", "Updated", ["ReadAt"]));

        bell.WaitForAssertion(() => bell.Find("[data-testid='unread']").TextContent.ShouldContain("3"));
    }
}

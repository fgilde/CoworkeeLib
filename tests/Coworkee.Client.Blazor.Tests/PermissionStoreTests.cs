using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Realtime;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class PermissionStoreTests
{
    [Fact]
    public async Task Loads_once_and_answers_from_cache()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(["Identity.Users.View"]);
        var store = new PermissionStore(api, new Realtime.RealtimeClient(new FakeRealtimeConnection()));

        (await store.HasAsync("Identity.Users.View")).ShouldBeTrue();
        (await store.HasAsync("Identity.Users.Manage")).ShouldBeFalse();

        await api.Received(1).GetMyPermissionsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_reloads()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(["A"], ["A", "B"]);
        var store = new PermissionStore(api, new Realtime.RealtimeClient(new FakeRealtimeConnection()));
        (await store.HasAsync("B")).ShouldBeFalse();

        store.Reset();

        (await store.HasAsync("B")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_push_on_a_permission_topic_reloads_once_per_burst()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns(["A"], ["A", "B"]);
        var connection = new FakeRealtimeConnection();
        var store = new PermissionStore(api, new Realtime.RealtimeClient(connection));
        var changed = 0;
        store.Changed += () => changed++;
        (await store.HasAsync("B")).ShouldBeFalse();
        connection.Invocations.ShouldBe([("Subscribe", RealtimeTopics.TenantPermissions), ("Subscribe", RealtimeTopics.GlobalPermissions)]);

        connection.Push(RealtimeTopics.TenantPermissions);
        connection.Push(RealtimeTopics.GlobalPermissions);
        await Task.Delay(600, TestContext.Current.CancellationToken);

        changed.ShouldBe(1);
        (await store.HasAsync("B")).ShouldBeTrue();
    }

    [Fact]
    public async Task Failed_load_means_no_permissions()
    {
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyCollection<string>>(_ => throw new ApiException(401, "auth.required", null));
        var store = new PermissionStore(api, new Realtime.RealtimeClient(new FakeRealtimeConnection()));

        (await store.HasAsync("A")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_server_that_does_not_answer_keeps_the_known_permissions_and_tries_again()
    {
        PermissionStore.RetryAfter = TimeSpan.FromMilliseconds(100);
        var api = Substitute.For<ICoworkeeApi>();
        var calls = 0;
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyCollection<string>>(_ => ++calls switch
        {
            1 => ["A"],
            2 => throw new ApiException(500, null, null),
            _ => ["A", "B"],
        });
        var store = new PermissionStore(api, new Realtime.RealtimeClient(new FakeRealtimeConnection()));
        var changed = 0;
        store.Changed += () => changed++;
        (await store.HasAsync("A")).ShouldBeTrue();

        // a change on the server while the database is down: the navigation must not empty
        store.Reset();
        (await store.HasAsync("A")).ShouldBeTrue("the last known permissions stay");
        store.Unavailable.ShouldBeTrue();

        await Task.Delay(400, TestContext.Current.CancellationToken);
        (await store.HasAsync("B")).ShouldBeTrue("asked again by itself");
        store.Unavailable.ShouldBeFalse();
        changed.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Without_an_answer_yet_the_store_says_it_is_unavailable_instead_of_no_permissions()
    {
        PermissionStore.RetryAfter = TimeSpan.FromSeconds(30);
        var api = Substitute.For<ICoworkeeApi>();
        api.GetMyPermissionsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyCollection<string>>(_ => throw new HttpRequestException("down"));
        var store = new PermissionStore(api, new Realtime.RealtimeClient(new FakeRealtimeConnection()));

        (await store.HasAsync("A")).ShouldBeFalse();

        store.Unavailable.ShouldBeTrue();
    }
}

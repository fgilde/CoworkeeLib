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
}

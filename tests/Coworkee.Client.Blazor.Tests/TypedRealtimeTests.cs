using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Realtime;
using Coworkee.Contracts.Notifications;
using Coworkee.Contracts.Realtime;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Tests;

public sealed class TypedRealtimeTests : ClientTestBase
{
    private readonly Guid _me = Guid.CreateVersion7();
    private readonly Guid _colleague = Guid.CreateVersion7();

    public TypedRealtimeTests() => AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", _me.ToString()), new Claim("sid", "mine"));

    private FakeRealtimeConnection Connection => Services.GetRequiredService<FakeRealtimeConnection>();

    private RealtimeClient Realtime => Services.GetRequiredService<RealtimeClient>();

    [Fact]
    public async Task Entity_changes_of_a_dto_follow_the_type_topic_of_its_entity_with_who_and_when()
    {
        var received = new List<EntityChanged<ProductDto>>();
        await using var subscription = await Realtime.On<EntityChanged<ProductDto>>(c => { received.Add(c); return Task.CompletedTask; });

        Connection.Invocations.ShouldContain((RealtimeHubMethods.Subscribe, "type:Product"));
        var at = DateTimeOffset.UtcNow;
        Connection.Push(Changed("type:Product", "Updated", _colleague, at));
        Connection.Push(new RealtimeEnvelope("type:Product", "SomethingElse", JsonSerializer.SerializeToElement(new { })));

        var change = received.ShouldHaveSingleItem();
        (change.EntityType, change.EntityId, change.Action, change.Actor!.UserId, change.At).ShouldBe(("Product", "42", "Updated", (Guid?)_colleague, (DateTimeOffset?)at));
        change.ChangedProperties.ShouldBe(["Name"]);
    }

    [Fact]
    public async Task Own_changes_can_be_skipped_and_a_topic_given_explicitly()
    {
        var received = new List<EntityChanged<ProductDto>>();
        await using var subscription = await Realtime.On<EntityChanged<ProductDto>>(c => { received.Add(c); return Task.CompletedTask; }, topic: "entity:Product:42", skipOwn: true);

        Connection.Push(Changed("entity:Product:42", "Updated", _me, DateTimeOffset.UtcNow));
        Connection.Push(Changed("entity:Product:42", "Deleted", _colleague, DateTimeOffset.UtcNow));

        received.ShouldHaveSingleItem().IsDeleted.ShouldBeTrue();
        Connection.Invocations.ShouldContain((RealtimeHubMethods.Subscribe, "entity:Product:42"));
        Connection.Invocations.ShouldNotContain((RealtimeHubMethods.Subscribe, "type:Product"));
    }

    [Fact]
    public async Task Custom_events_arrive_by_their_stable_name_on_every_topic_a_target_reaches_and_come_back_after_a_reconnect()
    {
        var received = new List<OrderApproved>();
        var subscription = await Realtime.On<OrderApproved>(e => { received.Add(e); return Task.CompletedTask; });

        Connection.Invocations.Where(i => i.Method == RealtimeHubMethods.Subscribe).Select(i => i.Topic).ShouldBe(
            [RealtimeTopics.User(_me), RealtimeTopics.TenantEvents, RealtimeTopics.GlobalEvents, RealtimeTopics.Session("mine")], ignoreOrder: true);
        Connection.Push(RealtimeTopics.TenantEvents, "orders.approved", new OrderApproved(7));
        Connection.Push(RealtimeTopics.User(_me), nameof(OrderApproved), new OrderApproved(8));
        received.Select(e => e.Number).ShouldBe([7]);

        await Connection.ReconnectAsync();
        Connection.Invocations.Count(i => i == (RealtimeHubMethods.Subscribe, RealtimeTopics.TenantEvents)).ShouldBe(2);

        await subscription.DisposeAsync();
        Connection.Invocations.ShouldContain((RealtimeHubMethods.Unsubscribe, RealtimeTopics.TenantEvents));
        Connection.Push(RealtimeTopics.TenantEvents, "orders.approved", new OrderApproved(9));
        received.Count.ShouldBe(1);
    }

    [Fact]
    public void Event_names_come_from_the_attribute_or_the_short_type_name()
    {
        EventNames.Of<OrderApproved>().ShouldBe("orders.approved");
        EventNames.Of<ToastPayload>().ShouldBe(ToastPayload.Event);
        EventNames.Of<ProductDto>().ShouldBe(nameof(ProductDto));
        EntityChanged<ProductDto>.Entity.ShouldBe("Product");
    }

    [Fact]
    public void The_component_form_calls_back_for_each_event()
    {
        var received = new List<EntityChanged<ProductDto>>();
        Render<RealtimeEvent<EntityChanged<ProductDto>>>(p => p.Add(r => r.OnEvent, (EntityChanged<ProductDto> c) => received.Add(c)).Add(r => r.SkipOwn, true));

        Connection.Push(Changed("type:Product", "Created", _colleague, DateTimeOffset.UtcNow));
        Connection.Push(Changed("type:Product", "Updated", _me, DateTimeOffset.UtcNow));

        received.ShouldHaveSingleItem().Action.ShouldBe("Created");
    }

    private static RealtimeEnvelope Changed(string topic, string action, Guid actor, DateTimeOffset at) =>
        new(topic, RealtimeEventTypes.EntityChanged, JsonSerializer.SerializeToElement(new EntityChangedPayload("Product", "42", action, action == "Updated" ? ["Name"] : []), JsonSerializerOptions.Web))
        {
            Id = Guid.CreateVersion7(),
            At = at,
            Actor = new ActorInfo(actor, "Someone"),
        };

    public sealed record ProductDto(Guid Id, string Name);

    [EventName("orders.approved")]
    public sealed record OrderApproved(int Number);
}

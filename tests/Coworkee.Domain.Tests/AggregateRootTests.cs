namespace Coworkee.Domain.Tests;

public sealed class AggregateRootTests
{
    [Fact]
    public void New_entities_get_unique_version7_ids()
    {
        var a = new Order();
        var b = new Order();

        a.Id.ShouldNotBe(Guid.Empty);
        a.Id.ShouldNotBe(b.Id);
        a.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Raised_events_are_collected_until_cleared()
    {
        var order = new Order();

        order.Place();
        order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPlaced>().OrderId.ShouldBe(order.Id);

        order.ClearDomainEvents();
        order.DomainEvents.ShouldBeEmpty();
    }

    private sealed record OrderPlaced(Guid OrderId) : IDomainEvent;

    private sealed class Order : AggregateRoot
    {
        public void Place() => Raise(new OrderPlaced(Id));
    }
}

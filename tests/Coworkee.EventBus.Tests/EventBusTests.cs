using Coworkee.Core.Security;
using Coworkee.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Coworkee.EventBus.Tests;

public sealed class EventBusTests(EventBusFixture database) : IAsyncLifetime
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await database.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Published_event_reaches_its_handler_once_the_unit_of_work_is_saved()
    {
        await using var provider = database.CreateServices(_clock);
        var processor = provider.GetRequiredService<OutboxProcessor<EventBusDbContext>>();
        var orderId = Guid.CreateVersion7();
        Guid? publisher;
        await using (var scope = provider.CreateAsyncScope())
        {
            publisher = scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId;
            await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new OrderPlaced(orderId), Ct);
            (await processor.ProcessBatchAsync(10, Ct)).ShouldBe(0);
            await scope.ServiceProvider.GetRequiredService<EventBusDbContext>().SaveChangesAsync(Ct);
        }

        (await processor.ProcessBatchAsync(10, Ct)).ShouldBe(1);

        var received = provider.GetRequiredService<Recorder>().Received.ShouldHaveSingleItem();
        received.Event.ShouldBe(new OrderPlaced(orderId));
        received.UserId.ShouldBe(publisher);
        (await QueryAsync(provider, db => db.Set<InboxMessage>().CountAsync(Ct))).ShouldBe(1);
        (await QueryAsync(provider, db => db.Set<OutboxMessage>().SingleAsync(Ct))).ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Redelivered_message_is_handled_once()
    {
        await using var provider = database.CreateServices(_clock);
        var delivery = provider.GetRequiredService<IntegrationEventDelivery>();
        var message = new IntegrationMessage(Guid.CreateVersion7(), IntegrationMessage.NameOf(typeof(OrderPlaced)), """{"OrderId":"0199a000-0000-7000-8000-000000000001"}""", null, null, null);

        await delivery.DeliverAsync(message, Ct);
        await delivery.DeliverAsync(message, Ct);

        provider.GetRequiredService<Recorder>().Received.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Failed_handler_is_retried_without_running_the_others_again()
    {
        await using var provider = database.CreateServices(_clock);
        var recorder = provider.GetRequiredService<Recorder>();
        recorder.FailuresLeft = 1;
        var processor = provider.GetRequiredService<OutboxProcessor<EventBusDbContext>>();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new OrderCancelled(Guid.CreateVersion7()), Ct);
            await scope.ServiceProvider.GetRequiredService<EventBusDbContext>().SaveChangesAsync(Ct);
        }

        await processor.ProcessBatchAsync(10, Ct);
        var failed = await QueryAsync(provider, db => db.Set<OutboxMessage>().SingleAsync(Ct));
        failed.Attempts.ShouldBe(1);
        failed.Error!.ShouldContain("down");
        recorder.Received.Select(r => r.Handler).ShouldBe([nameof(SteadyCancelledHandler)]);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await processor.ProcessBatchAsync(10, Ct);

        recorder.Received.Select(r => r.Handler).ShouldBe([nameof(SteadyCancelledHandler), nameof(FlakyCancelledHandler)]);
        (await QueryAsync(provider, db => db.Set<OutboxMessage>().SingleAsync(Ct))).ProcessedAt.ShouldNotBeNull();
    }

    private static async Task<T> QueryAsync<T>(IServiceProvider provider, Func<EventBusDbContext, Task<T>> query)
    {
        await using var scope = provider.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<EventBusDbContext>());
    }
}

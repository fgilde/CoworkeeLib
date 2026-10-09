using System.Collections.Concurrent;
using Coworkee.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Coworkee.EventBus.RabbitMq.Tests;

public sealed record ShipmentArrived(string Number) : IIntegrationEvent;

public sealed record ShipmentLost(string Number) : IIntegrationEvent;

public sealed class Recorder
{
    public ConcurrentQueue<object> Received { get; } = [];
}

internal sealed class ShipmentArrivedHandler(Recorder recorder) : IIntegrationEventHandler<ShipmentArrived>
{
    public Task HandleAsync(ShipmentArrived integrationEvent, CancellationToken cancellationToken)
    {
        recorder.Received.Enqueue(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class ShipmentLostHandler(Recorder recorder) : IIntegrationEventHandler<ShipmentLost>
{
    public Task HandleAsync(ShipmentLost integrationEvent, CancellationToken cancellationToken)
    {
        recorder.Received.Enqueue(integrationEvent);
        throw new InvalidOperationException("broken");
    }
}

public sealed class RabbitMqTests(RabbitMqFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Event_travels_from_the_outbox_through_rabbitmq_to_the_handler()
    {
        var queue = "arrived-" + Guid.NewGuid().ToString("N");
        await using var provider = await StartAsync(queue);

        await PublishAsync(provider, new ShipmentArrived("S1"));

        await WaitUntilAsync(() => !provider.GetRequiredService<Recorder>().Received.IsEmpty);
        provider.GetRequiredService<Recorder>().Received.ShouldHaveSingleItem().ShouldBe(new ShipmentArrived("S1"));
    }

    [Fact]
    public async Task Event_whose_handler_keeps_failing_ends_in_the_dead_letter_queue()
    {
        var queue = "lost-" + Guid.NewGuid().ToString("N");
        await using var provider = await StartAsync(queue);

        await PublishAsync(provider, new ShipmentLost("S2"));

        await using var channel = await provider.GetRequiredService<IConnection>().CreateChannelAsync(cancellationToken: Ct);
        await WaitUntilAsync(async () => await channel.MessageCountAsync(queue + ".dead", Ct) == 1);
        provider.GetRequiredService<Recorder>().Received.Count.ShouldBe(2);
    }

    private async Task<ServiceProvider> StartAsync(string queue)
    {
        var provider = fixture.CreateServices(queue);
        foreach (var consumer in provider.GetServices<IHostedService>().OfType<RabbitMqConsumer>())
        {
            await consumer.StartAsync(Ct);
        }

        // events are only routed once the queue is bound
        var connection = provider.GetRequiredService<IConnection>();
        await WaitUntilAsync(async () =>
        {
            await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
            try
            {
                return await channel.ConsumerCountAsync(queue, Ct) > 0;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                return false;
            }
        });
        return provider;
    }

    private static async Task PublishAsync(IServiceProvider provider, IIntegrationEvent integrationEvent)
    {
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(integrationEvent, Ct);
            await scope.ServiceProvider.GetRequiredService<RabbitDbContext>().SaveChangesAsync(Ct);
        }

        (await provider.GetRequiredService<OutboxProcessor<RabbitDbContext>>().ProcessBatchAsync(10, Ct)).ShouldBe(1);
    }

    private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 100 && !await condition(); attempt++)
        {
            await Task.Delay(100, Ct);
        }

        (await condition()).ShouldBeTrue();
    }
}

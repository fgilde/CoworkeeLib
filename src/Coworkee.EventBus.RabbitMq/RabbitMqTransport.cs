using System.Text;
using RabbitMQ.Client;

namespace Coworkee.EventBus.RabbitMq;

/// <summary>Publishes to a direct exchange keyed by event type and waits for the broker confirm, so the outbox only marks confirmed messages.</summary>
internal sealed class RabbitMqTransport(IConnection connection, RabbitMqOptions options) : IEventTransport, IAsyncDisposable
{
    public const string TenantHeader = "coworkee-tenant";
    public const string ActorHeader = "coworkee-actor";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task SendAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not { IsOpen: true })
            {
                _channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
                await _channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Direct, durable: true, cancellationToken: cancellationToken);
            }

            var properties = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                Type = message.EventType,
                CorrelationId = message.CorrelationId,
                ContentType = "application/json",
                Persistent = true,
                Headers = new Dictionary<string, object?> { [TenantHeader] = message.TenantId?.ToString(), [ActorHeader] = message.ActorId?.ToString() },
            };
            await _channel.BasicPublishAsync(options.Exchange, message.EventType, mandatory: false, properties, Encoding.UTF8.GetBytes(message.Payload), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }
}

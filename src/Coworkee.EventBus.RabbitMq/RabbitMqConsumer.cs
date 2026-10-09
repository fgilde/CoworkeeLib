using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Coworkee.EventBus.RabbitMq;

/// <summary>
/// Consumes the queue of this app, bound to every event type it has a handler for. A failed message goes back to the end of the queue
/// with its attempt count; after <see cref="RabbitMqOptions.MaxDeliveries"/> it moves to "{Queue}.dead".
/// </summary>
internal sealed partial class RabbitMqConsumer(
    IConnection connection, RabbitMqOptions options, IntegrationEventDelivery delivery, IReadOnlyCollection<string> eventTypes, ILogger<RabbitMqConsumer> logger)
    : BackgroundService
{
    private const string AttemptsHeader = "coworkee-attempts";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (eventTypes.Count == 0)
        {
            return;
        }

        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
        var (queue, deadLetters) = (options.Queue!, options.Queue + ".dead");
        await channel.ExchangeDeclareAsync(options.Exchange, ExchangeType.Direct, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(deadLetters, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

        // the delivery limit only catches messages that crash the consumer; handler failures are counted in the header
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = options.MaxDeliveries,
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = deadLetters,
        }, cancellationToken: stoppingToken);
        foreach (var eventType in eventTypes)
        {
            await channel.QueueBindAsync(queue, options.Exchange, eventType, cancellationToken: stoppingToken);
        }

        await channel.BasicQosAsync(0, options.Prefetch, global: false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivered) =>
        {
            try
            {
                await delivery.DeliverAsync(ToMessage(delivered), stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var attempts = Attempts(delivered.BasicProperties) + 1;
                LogFailed(exception, delivered.BasicProperties.MessageId, attempts);

                // ponytail: retried right away behind the queued messages, add delayed redelivery when handlers wait on slow recovering dependencies
                var properties = new BasicProperties(delivered.BasicProperties)
                {
                    Headers = new Dictionary<string, object?>(delivered.BasicProperties.Headers ?? new Dictionary<string, object?>()) { [AttemptsHeader] = attempts },
                };
                await channel.BasicPublishAsync(string.Empty, attempts >= options.MaxDeliveries ? deadLetters : queue, mandatory: false, properties, delivered.Body, stoppingToken);
            }

            await channel.BasicAckAsync(delivered.DeliveryTag, multiple: false, stoppingToken);
        };
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static IntegrationMessage ToMessage(BasicDeliverEventArgs delivered)
    {
        var properties = delivered.BasicProperties;
        return new IntegrationMessage(
            Guid.Parse(properties.MessageId!),
            properties.Type!,
            Encoding.UTF8.GetString(delivered.Body.Span),
            Header(properties, RabbitMqTransport.TenantHeader),
            Header(properties, RabbitMqTransport.ActorHeader),
            properties.CorrelationId);
    }

    private static Guid? Header(IReadOnlyBasicProperties properties, string name) =>
        properties.Headers?.TryGetValue(name, out var value) == true && value is byte[] bytes ? Guid.Parse(Encoding.UTF8.GetString(bytes)) : null;

    private static int Attempts(IReadOnlyBasicProperties properties) =>
        properties.Headers?.TryGetValue(AttemptsHeader, out var value) == true && value is int attempts ? attempts : 0;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Integration event {MessageId} failed (attempt {Attempts})")]
    private partial void LogFailed(Exception exception, string? messageId, int attempts);
}

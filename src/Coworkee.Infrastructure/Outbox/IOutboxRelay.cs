namespace Coworkee.Infrastructure.Outbox;

/// <summary>Gets every processed outbox message after its domain event handlers ran; throwing retries the message.</summary>
public interface IOutboxRelay
{
    Task RelayAsync(OutboxMessage message, object payload, CancellationToken cancellationToken);
}

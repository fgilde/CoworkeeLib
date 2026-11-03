namespace Coworkee.Webhooks;

/// <summary>Sends an app event to every active endpoint that chose it; it is stored with the unit of work and leaves after the commit.</summary>
public interface IWebhookPublisher
{
    /// <param name="type">E.g. "order.shipped"; list it with <see cref="WebhookOptions.Event"/> so admins can pick it.</param>
    /// <param name="data">Sent as it is: leave out what not every receiving admin may read.</param>
    Task PublishAsync(string type, object data, CancellationToken cancellationToken = default);
}

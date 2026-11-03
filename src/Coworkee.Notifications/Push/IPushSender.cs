namespace Coworkee.Notifications.Push;

/// <summary>Delivers an encrypted payload to one push subscription; replace it to send through a service of your own.</summary>
public interface IPushSender
{
    Task<PushResult> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken);
}

namespace Coworkee.Notifications.Push;

public enum PushResult
{
    Sent,

    /// <summary>The push service no longer knows the subscription: the device unsubscribed or the browser data was cleared.</summary>
    Gone,

    Failed,
}

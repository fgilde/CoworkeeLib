namespace Coworkee.Contracts.Webhooks;

public static class WebhookEventTypes
{
    /// <summary>Sent by "Send test event", whatever events the endpoint chose.</summary>
    public const string Test = "webhook.test";

    /// <summary>The endpoint takes every event.</summary>
    public const string All = "*";
}

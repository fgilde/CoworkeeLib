namespace Coworkee.Webhooks;

public sealed class WebhookBlockedException(string message) : Exception(message);

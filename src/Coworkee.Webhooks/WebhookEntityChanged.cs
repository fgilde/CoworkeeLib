namespace Coworkee.Webhooks;

public sealed record WebhookEntityChanged(string EntityType, string Key, string Action);

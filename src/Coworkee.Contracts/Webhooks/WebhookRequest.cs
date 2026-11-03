namespace Coworkee.Contracts.Webhooks;

/// <param name="Secret">Null keeps the stored secret, a new endpoint gets a generated one.</param>
public sealed record WebhookRequest(string Url, IReadOnlyList<string> Events, string? Description = null, bool IsActive = true, string? Secret = null);

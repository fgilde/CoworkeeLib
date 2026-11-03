using System.Text.Json;

namespace Coworkee.Webhooks;

public sealed record WebhookAppEvent(string Type, JsonElement Data);

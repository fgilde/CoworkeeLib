using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Webhooks;

[JsonConverter(typeof(JsonStringEnumConverter<WebhookDeliveryStatus>))]
public enum WebhookDeliveryStatus
{
    Pending,
    Succeeded,
    Failed,
}

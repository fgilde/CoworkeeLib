using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coworkee.Webhooks;

internal static class WebhookBody
{
    public static string Create(Guid id, string type, DateTimeOffset time, Guid tenantId, JsonNode? data) =>
        new JsonObject { ["id"] = id, ["type"] = type, ["time"] = time, ["tenant"] = tenantId, ["data"] = data }.ToJsonString(JsonSerializerOptions.Web);
}

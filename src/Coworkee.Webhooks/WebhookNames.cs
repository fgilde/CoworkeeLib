using System.Text.Json;
using Coworkee.OData;

namespace Coworkee.Webhooks;

internal static class WebhookNames
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";

    public static string EntitySet(ODataEntityRegistry registry, Type entityType) =>
        registry.Find(entityType)?.EntitySet ?? throw new InvalidOperationException($"{entityType.Name} sends webhooks but is no OData set: register it with AddODataEntity.");

    /// <summary>"Products" and "created" become "products.created", "OrderLines" becomes "order_lines.…".</summary>
    public static string EventType(string entitySet, string action) => $"{JsonNamingPolicy.SnakeCaseLower.ConvertName(entitySet)}.{action}";
}

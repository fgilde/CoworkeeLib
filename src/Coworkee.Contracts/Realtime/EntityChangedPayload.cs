using System.Text.Json;

namespace Coworkee.Contracts.Realtime;

public sealed record EntityChangedPayload(string EntityType, string EntityId, string Action, IReadOnlyList<string> ChangedProperties)
{
    public const string Created = "Created";
    public const string Updated = "Updated";

    /// <summary>Removed, also by a soft delete.</summary>
    public const string Deleted = "Deleted";

    public bool IsDeleted => Action == Deleted;

    public static EntityChangedPayload? From(RealtimeEnvelope envelope) =>
        envelope.Type == RealtimeEventTypes.EntityChanged ? envelope.Payload.Deserialize<EntityChangedPayload>(JsonSerializerOptions.Web) : null;
}

using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Auditing;

[JsonConverter(typeof(JsonStringEnumConverter<EntityHistoryRestoreMode>))]
public enum EntityHistoryRestoreMode
{
    /// <summary>The record as it was right after the entry.</summary>
    After,

    /// <summary>The record as it was right before the entry.</summary>
    Before,

    /// <summary>Only the fields of the entry go back; later changes of other fields stay.</summary>
    Undo,
}

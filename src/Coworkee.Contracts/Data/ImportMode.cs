using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<ImportMode>))]
public enum ImportMode
{
    Create,
    Update,
    Upsert,
}

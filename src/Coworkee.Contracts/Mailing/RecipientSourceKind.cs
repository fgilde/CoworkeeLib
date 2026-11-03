using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Mailing;

[JsonConverter(typeof(JsonStringEnumConverter<RecipientSourceKind>))]
public enum RecipientSourceKind
{
    Preset,
    Query,
    Manual,
}

using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

[JsonConverter(typeof(JsonStringEnumConverter<FacetValueKind>))]
public enum FacetValueKind
{
    Text,
    Number,
    Date,
    Boolean,
    Enum,
    Flags,
    Reference,
}

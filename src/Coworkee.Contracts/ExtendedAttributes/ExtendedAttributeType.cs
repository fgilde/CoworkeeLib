using System.Text.Json.Serialization;

namespace Coworkee.Contracts.ExtendedAttributes;

[JsonConverter(typeof(JsonStringEnumConverter<ExtendedAttributeType>))]
public enum ExtendedAttributeType
{
    Decimal = 1,
    Text = 2,
    DateTime = 3,
    Json = 4,
}

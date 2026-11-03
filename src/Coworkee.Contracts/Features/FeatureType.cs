using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Features;

[JsonConverter(typeof(JsonStringEnumConverter<FeatureType>))]
public enum FeatureType
{
    Bool,
    Int,
    String,
}

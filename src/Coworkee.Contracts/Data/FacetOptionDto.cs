using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

public sealed record FacetOptionDto(JsonElement Value, string Label, bool Enabled, int Count, [property: JsonPropertyName("o-data")] string OData)
{
    /// <summary>Extras of an option: <c>color</c> for swatches, <c>from</c> and <c>to</c> of a histogram bucket.</summary>
    [JsonPropertyName("meta")]
    public IReadOnlyDictionary<string, string>? Meta { get; init; }
}

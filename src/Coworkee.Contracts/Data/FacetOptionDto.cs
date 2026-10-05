using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

public sealed record FacetOptionDto(JsonElement Value, string Label, bool Enabled, int Count, [property: JsonPropertyName("o-data")] string OData);

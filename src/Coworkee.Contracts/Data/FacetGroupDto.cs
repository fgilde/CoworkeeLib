using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Data;

public sealed record FacetGroupDto(
    string Key,
    string Label,
    string Field,
    FacetKind Type,
    [property: JsonPropertyName("group-operator")] FacetOperator GroupOperator,
    IReadOnlyList<FacetOptionDto> Options,
    [property: JsonPropertyName("multi-select")] bool MultiSelect,
    int Order);

namespace Coworkee.Contracts.Features;

public sealed record FeatureGroupDto(string Name, string DisplayName, IReadOnlyList<FeatureDefinitionDto> Features);

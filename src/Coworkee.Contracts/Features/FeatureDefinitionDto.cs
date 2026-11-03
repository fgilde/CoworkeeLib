namespace Coworkee.Contracts.Features;

public sealed record FeatureDefinitionDto(string Name, string DisplayName, string? Description, FeatureType Type, string? DefaultValue);

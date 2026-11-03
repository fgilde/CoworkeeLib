namespace Coworkee.Features;

public interface IFeatureDefinitionManager
{
    IReadOnlyList<FeatureDefinition> All { get; }

    IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    FeatureDefinition? Find(string name);
}

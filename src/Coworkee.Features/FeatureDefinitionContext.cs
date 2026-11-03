namespace Coworkee.Features;

public sealed class FeatureDefinitionContext
{
    internal List<(string Name, string DisplayName)> Groups { get; } = [];

    internal List<FeatureDefinition> Definitions { get; } = [];

    public FeatureGroupBuilder Group(string name, string displayName)
    {
        if (!Groups.Exists(g => g.Name == name))
        {
            Groups.Add((name, displayName));
        }

        return new FeatureGroupBuilder(this, name);
    }
}

namespace Coworkee.Features;

internal sealed class FeatureDefinitionManager : IFeatureDefinitionManager
{
    private readonly Dictionary<string, FeatureDefinition> _byName;

    public FeatureDefinitionManager(IEnumerable<IFeatureDefinitionContributor> contributors)
    {
        var context = new FeatureDefinitionContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        All = context.Definitions;
        Groups = context.Groups;
        _byName = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);
        foreach (var definition in All)
        {
            if (!_byName.TryAdd(definition.Name, definition))
            {
                throw new InvalidOperationException($"Feature '{definition.Name}' is defined twice.");
            }
        }
    }

    public IReadOnlyList<FeatureDefinition> All { get; }

    public IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    public FeatureDefinition? Find(string name) => _byName.GetValueOrDefault(name);
}

using Coworkee.Contracts.Features;

namespace Coworkee.Features;

public sealed class FeatureGroupBuilder
{
    private readonly FeatureDefinitionContext _context;
    private readonly string _group;

    internal FeatureGroupBuilder(FeatureDefinitionContext context, string group)
    {
        _context = context;
        _group = group;
    }

    public FeatureGroupBuilder Add(string name, string displayName, FeatureType type = FeatureType.Bool, string? defaultValue = null, string? description = null)
    {
        _context.Definitions.Add(new FeatureDefinition(name, _group, displayName, type, defaultValue ?? (type == FeatureType.Bool ? "false" : null), description));
        return this;
    }
}

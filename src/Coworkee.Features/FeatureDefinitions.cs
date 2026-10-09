using System.Globalization;
using Coworkee.Contracts.Features;
using Coworkee.Core.Results;

namespace Coworkee.Features;

public sealed record FeatureDefinition(string Name, string Group, string DisplayName, FeatureType Type, string? DefaultValue, string? Description)
{
    public string? Validate(string value) => Type switch
    {
        FeatureType.Bool when !bool.TryParse(value, out _) => "Expected true or false.",
        FeatureType.Int when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => "Expected a whole number.",
        _ => null,
    };
}

/// <summary>Modules describe the features an edition or a tenant can switch on, like settings.</summary>
public interface IFeatureDefinitionContributor
{
    void Define(FeatureDefinitionContext context);
}

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

public interface IFeatureDefinitionManager
{
    IReadOnlyList<FeatureDefinition> All { get; }

    IReadOnlyList<(string Name, string DisplayName)> Groups { get; }

    FeatureDefinition? Find(string name);
}

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

internal static class FeatureValidation
{
    public static Error? Validate(this IFeatureDefinitionManager definitions, IReadOnlyDictionary<string, string>? values)
    {
        foreach (var (name, value) in values ?? new Dictionary<string, string>())
        {
            if (definitions.Find(name) is not { } definition)
            {
                return Error.Validation(name, $"Feature '{name}' is not defined.");
            }

            if (definition.Validate(value) is { } message)
            {
                return Error.Validation(name, message);
            }
        }

        return null;
    }
}

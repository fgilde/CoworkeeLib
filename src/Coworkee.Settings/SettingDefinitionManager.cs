using Microsoft.Extensions.Configuration;

namespace Coworkee.Settings;

internal sealed class SettingDefinitionManager : ISettingDefinitionManager
{
    private readonly Dictionary<string, SettingDefinition> _byName;

    public SettingDefinitionManager(IEnumerable<ISettingDefinitionContributor> contributors, IConfiguration configuration)
    {
        var context = new SettingDefinitionContext();
        foreach (var contributor in contributors)
        {
            contributor.Define(context);
        }

        var defaults = configuration.GetSection("Coworkee:Settings:Defaults");
        All = context.Definitions
            .Select(d => defaults[d.Name] is { } configured ? d with { DefaultValue = configured } : d)
            .ToList();
        Groups = context.Groups;
        _byName = All.ToDictionary(d => d.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<SettingDefinition> All { get; }

    public IReadOnlyList<SettingGroup> Groups { get; }

    public SettingDefinition? Find(string name) => _byName.GetValueOrDefault(name);
}

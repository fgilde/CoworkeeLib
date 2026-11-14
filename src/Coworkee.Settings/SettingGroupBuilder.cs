using Coworkee.Contracts.Settings;

namespace Coworkee.Settings;

public sealed class SettingGroupBuilder
{
    private readonly SettingDefinitionContext _context;
    private readonly string _group;

    internal SettingGroupBuilder(SettingDefinitionContext context, string group)
    {
        _context = context;
        _group = group;
    }

    public SettingGroupBuilder Add(
        string name,
        string displayName,
        SettingType type,
        SettingScope[] scopes,
        string? defaultValue = null,
        bool visibleToClient = false,
        string? description = null,
        string[]? choices = null,
        SettingCondition? enabledWhen = null,
        string? permission = null,
        string? fixedValue = null)
    {
        _context.Definitions.Add(new SettingDefinition(name, _group, displayName, type, scopes, defaultValue, visibleToClient, description, choices, enabledWhen, permission, fixedValue));
        return this;
    }
}

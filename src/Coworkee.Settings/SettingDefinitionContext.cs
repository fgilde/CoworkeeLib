namespace Coworkee.Settings;

public sealed class SettingDefinitionContext
{
    internal List<SettingGroup> Groups { get; } = [];

    internal List<SettingDefinition> Definitions { get; } = [];

    public SettingGroupBuilder Group(string name, string displayName, string? page = null)
    {
        if (!Groups.Exists(g => g.Name == name))
        {
            Groups.Add(new SettingGroup(name, displayName, page));
        }

        return new SettingGroupBuilder(this, name);
    }
}

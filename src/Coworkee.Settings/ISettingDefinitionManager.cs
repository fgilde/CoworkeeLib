namespace Coworkee.Settings;

public interface ISettingDefinitionManager
{
    IReadOnlyList<SettingDefinition> All { get; }

    IReadOnlyList<SettingGroup> Groups { get; }

    SettingDefinition? Find(string name);
}

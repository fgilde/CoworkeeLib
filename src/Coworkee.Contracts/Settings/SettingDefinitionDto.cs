namespace Coworkee.Contracts.Settings;

public sealed record SettingDefinitionDto(
    string Name, string DisplayName, string? Description, SettingType Type, string? DefaultValue, IReadOnlyList<SettingScope> Scopes, IReadOnlyList<string>? Choices,
    SettingCondition? EnabledWhen = null);

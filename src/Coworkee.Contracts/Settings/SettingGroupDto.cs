namespace Coworkee.Contracts.Settings;

public sealed record SettingGroupDto(string Name, string DisplayName, IReadOnlyList<SettingDefinitionDto> Settings, string? Page = null);

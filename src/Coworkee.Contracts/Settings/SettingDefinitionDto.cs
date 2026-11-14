namespace Coworkee.Contracts.Settings;

/// <param name="FixedValue">The value the host fixes in its configuration (e.g. <c>ModeOverride</c>): it applies whatever is stored; forms show it read-only.</param>
public sealed record SettingDefinitionDto(
    string Name, string DisplayName, string? Description, SettingType Type, string? DefaultValue, IReadOnlyList<SettingScope> Scopes, IReadOnlyList<string>? Choices,
    SettingCondition? EnabledWhen = null, string? FixedValue = null);

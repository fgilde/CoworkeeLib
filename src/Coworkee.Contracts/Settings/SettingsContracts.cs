using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SettingScope>))]
public enum SettingScope
{
    Global,
    Tenant,
    User,
}

[JsonConverter(typeof(JsonStringEnumConverter<SettingType>))]
public enum SettingType
{
    String,
    Text,
    Bool,
    Int,
    Secret,
    Choice,
}

public static class SettingsPermissions
{
    public const string GroupName = "Settings";
    public const string Manage = "Settings.Manage";
}

public sealed record SettingDefinitionDto(
    string Name, string DisplayName, string? Description, SettingType Type, string? DefaultValue, IReadOnlyList<SettingScope> Scopes, IReadOnlyList<string>? Choices);

public sealed record SettingGroupDto(string Name, string DisplayName, IReadOnlyList<SettingDefinitionDto> Settings);

public sealed record SettingValueDto(string Name, string? Value, bool HasValue);

public sealed record SetSettingsRequest(IReadOnlyDictionary<string, string?> Values);

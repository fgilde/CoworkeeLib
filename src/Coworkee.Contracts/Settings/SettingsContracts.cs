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

/// <summary>A typed configuration section the app lets admins edit (its class is usually generated from a JSON file).</summary>
public sealed record AppConfigurationDto(string Section, string Title);

/// <summary>
/// The section as it applies now and as it would without changes made in the app; secrets come masked, hidden values not at all.
/// <see cref="Locked"/> and <see cref="Hidden"/> are property paths below the section ("Jobs:WorkerCount").
/// </summary>
public sealed record AppConfigurationValuesDto(
    string Section, System.Text.Json.JsonElement Values, System.Text.Json.JsonElement Defaults, IReadOnlyList<string> ChangedKeys,
    IReadOnlyList<string>? Locked = null, IReadOnlyList<string>? Hidden = null);

public static class AppConfigurationMask
{
    /// <summary>Stands for a stored secret; sending it back keeps the secret.</summary>
    public const string Value = "********";
}

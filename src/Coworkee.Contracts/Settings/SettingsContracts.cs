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
    Language,
}

public static class TableSettings
{
    /// <summary>The user's saved table views as JSON, keyed by table.</summary>
    public const string Views = "Table.Views";

    /// <summary>How tables without their own page mode show many rows: <see cref="Pager"/> or <see cref="EndlessScroll"/>.</summary>
    public const string DefaultPageMode = "DataTable.DefaultPageMode";

    public const string Pager = "Pager";

    public const string EndlessScroll = "EndlessScroll";

    /// <summary>The user's choice per table with a local copy, "Client" or "Server", as JSON keyed by table.</summary>
    public const string ClientEntityModes = "Table.ClientEntityModes";

    /// <summary>The user's view choices of report widgets as JSON, keyed by report and widget (see <c>ReportViews.Key</c>).</summary>
    public const string ReportViews = "Reports.Views";
}

/// <summary>The engine of every rich text editor of the library: <see cref="TipTap"/> (default) or <see cref="Quill"/>.</summary>
public static class EditorSettings
{
    public const string RichTextEditor = "Editors.RichTextEditor";

    public const string TipTap = "TipTap";

    public const string Quill = "Quill";
}

/// <summary>The cloud pickers of every upload field; a picker shows when it is enabled and has its client id or app key.</summary>
public static class FilePickerSettings
{
    public const string GoogleDriveEnabled = "FilePickers.GoogleDrive.Enabled";
    public const string GoogleDriveClientId = "FilePickers.GoogleDrive.ClientId";
    public const string OneDriveEnabled = "FilePickers.OneDrive.Enabled";
    public const string OneDriveClientId = "FilePickers.OneDrive.ClientId";
    public const string DropboxEnabled = "FilePickers.Dropbox.Enabled";
    public const string DropboxAppKey = "FilePickers.Dropbox.AppKey";
}

public static class SettingsPermissions
{
    public const string GroupName = "Settings";
    public const string Manage = "Settings.Manage";

    /// <summary>Changes the head script (<see cref="HeadScriptSettings"/>); defined only in apps that turn the head script on.</summary>
    public const string HeadScript = "Settings.HeadScript";
}

/// <summary>
/// HTML with script blocks (a tag manager, analytics) the Blazor app adds to its page once it starts, and other clients read anonymously
/// (<c>GET /api/v1/settings/head-script</c>). Off unless the app calls <c>AddCoworkeeHeadScript</c>; only <see cref="SettingsPermissions.HeadScript"/> changes it.
/// </summary>
public static class HeadScriptSettings
{
    public const string Enabled = "HeadScript.Enabled";
    public const string Value = "HeadScript.Value";

    /// <summary>The longest value the server takes.</summary>
    public const int MaxLength = 50_000;
}

/// <summary>The head script as it applies to the caller: of the organisation, else of the installation.</summary>
public sealed record HeadScriptDto(bool Enabled, string Value);

/// <summary>Sets the head script of the caller's organisation, or of the installation when the caller is in the system organisation.</summary>
public sealed record SetHeadScriptRequest(bool Enabled, string? Value);

/// <summary>A setting applies only while <see cref="Setting"/> holds one of <see cref="Values"/> (bool as true/false); otherwise its field is disabled.</summary>
public sealed record SettingCondition(string Setting, IReadOnlyList<string> Values)
{
    public static SettingCondition IsTrue(string setting) => new(setting, ["true"]);
}

public sealed record SettingDefinitionDto(
    string Name, string DisplayName, string? Description, SettingType Type, string? DefaultValue, IReadOnlyList<SettingScope> Scopes, IReadOnlyList<string>? Choices,
    SettingCondition? EnabledWhen = null);

public sealed record SettingGroupDto(string Name, string DisplayName, IReadOnlyList<SettingDefinitionDto> Settings, string? Page = null);

public sealed record SettingValueDto(string Name, string? Value, bool HasValue);

public sealed record SetSettingsRequest(IReadOnlyDictionary<string, string?> Values);

/// <summary>A typed configuration section the app lets admins edit (its class is usually generated from a JSON file).</summary>
public sealed record AppConfigurationDto(string Section, string Title);

/// <summary>
/// The section as it applies now and as it would without changes made in the app; secrets come masked, hidden values not at all.
/// <see cref="Locked"/>, <see cref="Hidden"/> and <see cref="Protected"/> (changeable after unlocking in the form) are property paths below the section ("Jobs:WorkerCount").
/// </summary>
public sealed record AppConfigurationValuesDto(
    string Section, System.Text.Json.JsonElement Values, System.Text.Json.JsonElement Defaults, IReadOnlyList<string> ChangedKeys,
    IReadOnlyList<string>? Locked = null, IReadOnlyList<string>? Hidden = null, IReadOnlyList<string>? Protected = null);

public static class AppConfigurationMask
{
    /// <summary>Stands for a stored secret; sending it back keeps the secret.</summary>
    public const string Value = "********";
}

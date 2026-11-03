namespace Coworkee.Contracts.Settings;

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

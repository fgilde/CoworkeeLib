namespace Coworkee.Contracts.Settings;

public static class SettingsPermissions
{
    public const string GroupName = "Settings";
    public const string Manage = "Settings.Manage";

    /// <summary>Changes the head script (<see cref="HeadScriptSettings"/>); defined only in apps that turn the head script on.</summary>
    public const string HeadScript = "Settings.HeadScript";

    /// <summary>Writes and deletes the organisation's global client values (<see cref="ClientValueScope.Global"/>); every user reads them and owns his own.</summary>
    public const string ManageGlobalClientValues = "Settings.ClientValues.ManageGlobal";
}

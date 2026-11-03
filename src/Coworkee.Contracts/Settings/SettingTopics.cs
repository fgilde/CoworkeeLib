namespace Coworkee.Contracts.Settings;

/// <summary>Realtime topics of saved settings: clients reload their settings, so open pages follow a change at once. User settings go to the user's topic.</summary>
public static class SettingTopics
{
    /// <summary>A global setting changed: every organisation.</summary>
    public const string Global = "global:settings";

    /// <summary>A setting of the organisation changed.</summary>
    public const string Tenant = "tenant:settings";
}

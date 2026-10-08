namespace Coworkee.Aspire.Settings;

public sealed class SettingsSection
{
    /// <summary>Defaults of runtime settings by key, e.g. "Mail.Smtp.Host".</summary>
    public Dictionary<string, string?> Defaults { get; } = [];
}

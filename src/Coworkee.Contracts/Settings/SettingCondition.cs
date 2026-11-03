namespace Coworkee.Contracts.Settings;

/// <summary>A setting applies only while <see cref="Setting"/> holds one of <see cref="Values"/> (bool as true/false); otherwise its field is disabled.</summary>
public sealed record SettingCondition(string Setting, IReadOnlyList<string> Values)
{
    public static SettingCondition IsTrue(string setting) => new(setting, ["true"]);
}

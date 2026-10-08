namespace Coworkee.Aspire.Settings;

/// <summary>The configuration tree of a Coworkee service; only used to name settings in a type safe way (<see cref="SettingPath"/>).</summary>
public sealed class CoworkeeSettings
{
    public CoworkeeSection Coworkee { get; } = new();

    public Dictionary<string, string> ConnectionStrings { get; } = [];
}

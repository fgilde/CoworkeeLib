namespace Coworkee.Contracts.Settings;

/// <summary>A line of a status section; <paramref name="Value"/> is a localizable text when <paramref name="Arguments"/> is not null, otherwise shown as it is.</summary>
public sealed record SystemStatusEntry(string Label, string Value, SystemStatusLevel Level = SystemStatusLevel.Info, IReadOnlyList<string>? Arguments = null, string? Detail = null)
{
    public static SystemStatusEntry Text(string label, string value, SystemStatusLevel level = SystemStatusLevel.Info) => new(label, value, level, []);
}

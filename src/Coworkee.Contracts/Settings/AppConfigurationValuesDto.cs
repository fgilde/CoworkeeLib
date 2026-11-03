namespace Coworkee.Contracts.Settings;

/// <summary>
/// The section as it applies now and as it would without changes made in the app; secrets come masked, hidden values not at all.
/// <see cref="Locked"/>, <see cref="Hidden"/> and <see cref="Protected"/> (changeable after unlocking in the form) are property paths below the section ("Jobs:WorkerCount").
/// </summary>
public sealed record AppConfigurationValuesDto(
    string Section, System.Text.Json.JsonElement Values, System.Text.Json.JsonElement Defaults, IReadOnlyList<string> ChangedKeys,
    IReadOnlyList<string>? Locked = null, IReadOnlyList<string>? Hidden = null, IReadOnlyList<string>? Protected = null);

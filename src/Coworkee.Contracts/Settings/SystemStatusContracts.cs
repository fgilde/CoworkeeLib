using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<SystemStatusLevel>))]
public enum SystemStatusLevel
{
    Info,
    Ok,
    Warning,
    Error,
}

/// <summary>A line of a status section; <paramref name="Value"/> is a localizable text when <paramref name="Arguments"/> is not null, otherwise shown as it is.</summary>
public sealed record SystemStatusEntry(string Label, string Value, SystemStatusLevel Level = SystemStatusLevel.Info, IReadOnlyList<string>? Arguments = null, string? Detail = null)
{
    public static SystemStatusEntry Text(string label, string value, SystemStatusLevel level = SystemStatusLevel.Info) => new(label, value, level, []);
}

/// <summary>Something that went wrong recently (a failed job, a mail that could not be sent), shown as it is.</summary>
public sealed record SystemStatusProblem(string Title, string Message, DateTimeOffset? At = null);

/// <summary>
/// A section of the System status page. <paramref name="Link"/> leads to details: a library page by its old address (the client resolves it) or,
/// with <paramref name="ExternalLink"/>, a page outside the app such as the job dashboard.
/// </summary>
public sealed record SystemStatusSection(
    string Key, string Title, IReadOnlyList<SystemStatusEntry> Entries, IReadOnlyList<SystemStatusProblem>? Problems = null, int Order = 0, string? Link = null,
    bool ExternalLink = false);

public sealed record SystemStatusDto(DateTimeOffset At, IReadOnlyList<SystemStatusSection> Sections);

namespace Coworkee.Contracts.Settings;

/// <summary>
/// A section of the System status page. <paramref name="Link"/> leads to details: a library page by its old address (the client resolves it) or,
/// with <paramref name="ExternalLink"/>, a page outside the app such as the job dashboard.
/// </summary>
public sealed record SystemStatusSection(
    string Key, string Title, IReadOnlyList<SystemStatusEntry> Entries, IReadOnlyList<SystemStatusProblem>? Problems = null, int Order = 0, string? Link = null,
    bool ExternalLink = false);

namespace Coworkee.Realtime.Watch;

/// <summary>
/// What one look of a watch found: the newest date it saw (where the next look starts), the changed rows, or <see cref="Many"/> when there were more than
/// the watch sends one by one (<see cref="Why"/> tells how many, for the log).
/// </summary>
internal sealed record RealtimeWatchRound(DateTimeOffset? Watermark, IReadOnlyList<RealtimeWatchChange> Changes, bool Many)
{
    public string? Why { get; init; }
}

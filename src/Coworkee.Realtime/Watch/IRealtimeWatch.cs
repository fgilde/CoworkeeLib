namespace Coworkee.Realtime.Watch;

/// <summary>One watched set (AddRealtimeWatch): reads the rows that changed since a date.</summary>
internal interface IRealtimeWatch
{
    Type EntityType { get; }

    TimeSpan Interval { get; }

    /// <param name="since">The watermark of the last look; null for the first look, which only takes the newest date.</param>
    Task<RealtimeWatchRound> ReadAsync(IServiceProvider services, DateTimeOffset? since, CancellationToken cancellationToken);
}

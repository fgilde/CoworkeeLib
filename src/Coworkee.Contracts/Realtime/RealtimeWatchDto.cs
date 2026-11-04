namespace Coworkee.Contracts.Realtime;

/// <summary>
/// A set watched for changes another system writes (AddRealtimeWatch): whether the watch looks right now, how often, and whether the caller may switch it.
/// </summary>
public sealed record RealtimeWatchDto(string EntityType, bool Enabled, TimeSpan Interval, bool CanManage);

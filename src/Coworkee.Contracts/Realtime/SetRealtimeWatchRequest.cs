namespace Coworkee.Contracts.Realtime;

/// <summary>Switches a watched set on or off (<see cref="RealtimePermissions.ManageWatches"/>).</summary>
public sealed record SetRealtimeWatchRequest(bool Enabled);

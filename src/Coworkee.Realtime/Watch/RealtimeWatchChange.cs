namespace Coworkee.Realtime.Watch;

/// <summary>A row a watch found changed: its key, its organisation (null for rows of every organisation) and created or updated.</summary>
internal sealed record RealtimeWatchChange(string Id, Guid? TenantId, string Action);

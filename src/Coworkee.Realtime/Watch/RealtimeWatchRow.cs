namespace Coworkee.Realtime.Watch;

/// <summary>What a look reads of a changed row: its key values, its organisation and its dates (not the row: a hub shipment has a hundred columns).</summary>
internal sealed record RealtimeWatchRow(object?[] Key, Guid? TenantId, DateTimeOffset?[] Dates, DateTimeOffset? Created);
